using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Souffleur.Domain.Ticketing;

namespace Souffleur.Infrastructure.Ticketing;

/// <summary>
/// Liest Veranstaltungen und Verkaufszahlen aus pretix.
/// </summary>
/// <remarks>
/// Nur lesend: souffleur schreibt nichts in den Vorverkauf zurueck. Der Weg
/// ueber die REST-Schnittstelle ist der einzige, der bei einem gehosteten
/// pretix offensteht -- auf dessen Server kommt niemand.
///
/// Uebernommen aus dem alten souffleur, samt der Entscheidungen, die dort an
/// echten Daten entstanden sind: Gezaehlt werden Bestellpositionen, nicht
/// "Kapazitaet minus frei".
/// </remarks>
public class PretixClient(HttpClient http)
{
    /// <summary>pretix laesst bis zu 200 Objekte je Seite zu.</summary>
    private const int PageSize = 200;

    /// <summary>Hoechstzahl gleichzeitiger Abfragen -- gehostet gelten Grenzen.</summary>
    private const int MaxParallel = 4;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Fragt die Instanz einmal an, ohne etwas auszuwerten. Fuer den Knopf
    /// "Verbindung prüfen": Wer gerade ein Token eingetragen hat, will wissen,
    /// ob es trägt, bevor er die Seite verlässt.
    /// </summary>
    public async Task<string?> CheckAsync(
        TicketingSettings settings,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await GetAsync<Paged<EventRow>>(
                settings, $"/organizers/{settings.Organizer}/events/?page_size=1", cancellationToken);
            return null;
        }
        catch (PretixException exception)
        {
            return exception.Message;
        }
        catch (HttpRequestException exception)
        {
            return $"Die Instanz ist nicht erreichbar: {exception.Message}";
        }
        catch (TaskCanceledException)
        {
            return "Die Instanz hat nicht innerhalb von zehn Sekunden geantwortet.";
        }
    }

    /// <summary>Alle Vorstellungen mit ihren Verkaufszahlen.</summary>
    public async Task<IReadOnlyList<Performance>> PerformancesAsync(
        TicketingSettings settings,
        CancellationToken cancellationToken = default)
    {
        string organizer = settings.Organizer
            ?? throw new PretixException("Ohne Veranstalter gibt es nichts abzufragen.");

        IReadOnlyList<(EventRow Event, Subevent? Subevent)> performances =
            await ListPerformancesAsync(settings, organizer, cancellationToken);

        Performance[] result = await WhenAllLimited(
            performances,
            p => LoadAsync(settings, organizer, p.Event, p.Subevent, cancellationToken),
            cancellationToken);

        return [.. result.OrderBy(p => p.Date)];
    }

    /// <summary>Loest Terminserien in ihre einzelnen Vorstellungen auf.</summary>
    private async Task<IReadOnlyList<(EventRow, Subevent?)>> ListPerformancesAsync(
        TicketingSettings settings,
        string organizer,
        CancellationToken cancellationToken)
    {
        List<EventRow> events = await GetAllAsync<EventRow>(
            settings, $"/organizers/{organizer}/events/", cancellationToken);

        List<(EventRow, Subevent?)>[] perEvent = await WhenAllLimited(
            events,
            async row =>
            {
                if (!row.HasSubevents)
                {
                    return new List<(EventRow, Subevent?)> { (row, null) };
                }

                List<Subevent> subevents = await GetAllAsync<Subevent>(
                    settings,
                    $"/organizers/{organizer}/events/{row.Slug}/subevents/",
                    cancellationToken);

                return [.. subevents.Where(s => s.Active).Select(s => (row, (Subevent?)s))];
            },
            cancellationToken);

        return [.. perEvent.SelectMany(list => list)];
    }

    private async Task<Performance> LoadAsync(
        TicketingSettings settings,
        string organizer,
        EventRow row,
        Subevent? subevent,
        CancellationToken cancellationToken)
    {
        string prefix = $"/organizers/{organizer}/events/{row.Slug}";
        int? subeventId = subevent?.Id;

        Task<List<QuotaRow>> quotas = GetAllAsync<QuotaRow>(
            settings, Scoped($"{prefix}/quotas/", subeventId), cancellationToken);

        // Plaetze nur holen, wenn das Event einen Saalplan hat -- sonst waere
        // es eine Abfrage ins Leere.
        Task<List<SeatRow>> seats = row.SeatingPlan is null
            ? Task.FromResult(new List<SeatRow>())
            : GetAllAsync<SeatRow>(settings, Scoped($"{prefix}/seats/", subeventId), cancellationToken);

        Task<(int Sold, decimal Revenue)> sales =
            SalesAsync(settings, prefix, subeventId, cancellationToken);

        await Task.WhenAll(quotas, seats, sales);

        // Der Saalplan ist die genauere Quelle: Er zaehlt echte Plaetze, statt
        // sie aus Kontingenten abzuleiten.
        int? fromSeats = PretixCapacity.FromSeats(
            [.. seats.Result.Select(s => new PretixSeat(s.Blocked))]);

        int? capacity;
        string? note;
        CapacitySource source;

        if (fromSeats is { } seated)
        {
            (capacity, note, source) = (seated, null, CapacitySource.SeatingPlan);
        }
        else
        {
            (capacity, note) = PretixCapacity.FromQuotas(
                [.. quotas.Result.Select(q => new PretixQuota(q.Size, q.Items, q.Variations))]);
            source = capacity is null ? CapacitySource.Unknown : CapacitySource.Quotas;
        }

        // Der Termin einer Serie steht im Subevent, nicht im Event. Der Name
        // auch -- sofern das Haus ihn dort pflegt; sonst gilt der Serientitel.
        string name = Localized(subevent?.Name) ?? Localized(row.Name) ?? row.Slug;
        DateTimeOffset date = subevent?.DateFrom ?? row.DateFrom;

        return new Performance(
            subevent is null ? row.Slug : $"{row.Slug}#{subevent.Id}",
            name,
            date,
            capacity,
            source,
            sales.Result.Sold,
            capacity is > 0 ? sales.Result.Sold * 100.0 / capacity.Value : null,
            sales.Result.Revenue,
            row.Currency,
            note);
    }

    /// <summary>
    /// Gebuchte Karten und Ticketerloes einer Vorstellung.
    /// </summary>
    /// <remarks>
    /// Bewusst nicht "Kapazitaet minus frei": `available_number` sinkt in
    /// pretix auch durch Warenkorb-Reservierungen, Sperrungen und
    /// Gutscheinkontingente -- die Auslastung fiele dadurch systematisch zu
    /// hoch aus. Gezaehlt werden die tatsaechlichen Bestellpositionen, bezahlt
    /// (p) und verbindlich bestellt (n), jeweils ohne Stornos.
    ///
    /// Bestellungen lassen sich nicht nach Termin filtern -- eine Bestellung
    /// kann Karten fuer mehrere Termine enthalten. Die Position ist die
    /// einzige Ebene, auf der sich Umsatz einer Vorstellung zuordnen laesst.
    /// </remarks>
    private async Task<(int Sold, decimal Revenue)> SalesAsync(
        TicketingSettings settings,
        string prefix,
        int? subeventId,
        CancellationToken cancellationToken)
    {
        string basePath = $"{prefix}/orderpositions/?canceled=false";

        List<PositionRow> paid = await GetAllAsync<PositionRow>(
            settings, Scoped($"{basePath}&order__status=p", subeventId), cancellationToken);

        List<PositionRow> pending = await GetAllAsync<PositionRow>(
            settings, Scoped($"{basePath}&order__status=n", subeventId), cancellationToken);

        // Nur bezahlte Positionen zaehlen als Erloes; offene sind noch nicht geflossen.
        decimal revenue = paid.Sum(position =>
            decimal.TryParse(
                position.Price,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out decimal value)
                ? value
                : 0m);

        return (paid.Count + pending.Count, revenue);
    }

    /// <summary>Haengt den Subevent-Filter an, wo einer noetig ist.</summary>
    public static string Scoped(string path, int? subeventId) =>
        subeventId is null ? path : $"{path}{(path.Contains('?') ? '&' : '?')}subevent={subeventId}";

    /// <summary>Folgt der Paginierung.</summary>
    private async Task<List<T>> GetAllAsync<T>(
        TicketingSettings settings,
        string path,
        CancellationToken cancellationToken)
    {
        List<T> all = [];
        char separator = path.Contains('?') ? '&' : '?';

        for (int page = 1; page <= 500; page++)
        {
            Paged<T> chunk = await GetAsync<Paged<T>>(
                settings, $"{path}{separator}page={page}&page_size={PageSize}", cancellationToken);

            all.AddRange(chunk.Results);

            if (chunk.Next is null)
            {
                return all;
            }
        }

        throw new PretixException(
            $"Die Seitenfolge bei {path} endet nicht — abgebrochen nach 500 Seiten.");
    }

    /// <summary>
    /// Eine Abfrage, mit Wiederholung.
    /// </summary>
    /// <remarks>
    /// Wiederholt wird nur, was sich durch Wiederholen beheben laesst:
    /// Netzfehler, Zeitueberschreitungen und Serverfehler. Ein 401 oder 404
    /// ist eine Fehlkonfiguration -- der wird sofort durchgereicht, statt
    /// dreimal zu warten.
    /// </remarks>
    private async Task<T> GetAsync<T>(
        TicketingSettings settings,
        string path,
        CancellationToken cancellationToken)
    {
        Exception? last = null;

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get, $"{settings.BaseUrl!.TrimEnd('/')}/api/v1{path}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Token", settings.Token);

                using HttpResponseMessage answer =
                    await http.SendAsync(request, cancellationToken);

                if (!answer.IsSuccessStatusCode)
                {
                    throw new PretixException(Explain(answer.StatusCode, path), answer.StatusCode);
                }

                return await answer.Content.ReadFromJsonAsync<T>(Json, cancellationToken)
                    ?? throw new PretixException($"Leere Antwort bei {path}.");
            }
            catch (PretixException exception) when (Retryable(exception.Status) && attempt < 3)
            {
                last = exception;
            }
            catch (HttpRequestException exception) when (attempt < 3)
            {
                last = exception;
            }

            await Task.Delay(attempt * 300, cancellationToken);
        }

        throw last ?? new PretixException($"Abfrage {path} ist gescheitert.");
    }

    private static bool Retryable(HttpStatusCode? status) =>
        status is null
        || status == HttpStatusCode.TooManyRequests
        || (int)status >= 500;

    private static string Explain(HttpStatusCode status, string path) => status switch
    {
        HttpStatusCode.Unauthorized => "Das Token wird nicht angenommen (401).",
        HttpStatusCode.Forbidden => "Das Token reicht für diese Abfrage nicht (403).",
        HttpStatusCode.NotFound =>
            $"Unter {path} liegt nichts — stimmt der Veranstalter-Kurzname? (404)",
        HttpStatusCode.TooManyRequests => "Die Instanz begrenzt die Abfragen (429).",
        _ => $"pretix antwortet mit {(int)status} bei {path}.",
    };

    /// <summary>Ein lokalisierter Name ist ein Objekt wie { de: "...", en: "..." }.</summary>
    private static string? Localized(Dictionary<string, string>? name)
    {
        if (name is null || name.Count == 0)
        {
            return null;
        }

        return name.TryGetValue("de", out string? german) ? german : name.Values.First();
    }

    /// <summary>Arbeitet die Liste ab, aber hoechstens vier Abfragen auf einmal.</summary>
    private static async Task<TResult[]> WhenAllLimited<TSource, TResult>(
        IReadOnlyList<TSource> items,
        Func<TSource, Task<TResult>> work,
        CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(MaxParallel);

        return await Task.WhenAll(items.Select(async item =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await work(item);
            }
            finally
            {
                gate.Release();
            }
        }));
    }

    private record Paged<T>(int Count, List<T> Results, string? Next);

    private record EventRow(
        string Slug,
        Dictionary<string, string>? Name,
        [property: JsonPropertyName("date_from")] DateTimeOffset DateFrom,
        string Currency,
        [property: JsonPropertyName("seating_plan")] int? SeatingPlan,
        [property: JsonPropertyName("has_subevents")] bool HasSubevents);

    private record Subevent(
        int Id,
        Dictionary<string, string>? Name,
        [property: JsonPropertyName("date_from")] DateTimeOffset DateFrom,
        bool Active);

    private record QuotaRow(int Id, int? Size, List<int> Items, List<int> Variations);

    private record SeatRow(int Id, bool Blocked);

    private record PositionRow(string Price);
}

public class PretixException(string message, HttpStatusCode? status = null)
    : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
}
