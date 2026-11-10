using Souffleur.Domain.Accounting;
using Souffleur.Domain.Common;
using Souffleur.Domain.Contacts;

namespace Souffleur.Domain.Touring;

/// <summary>
/// Wie weit ein Gastspiel ist -- von der ersten Anfrage bis zur Abrechnung.
/// </summary>
/// <remarks>
/// Die Stufen folgen dem Vorgang, nicht einer Wunschliste: Ein Gastgeber
/// fragt an, das Haus macht ein Angebot, jemand sagt zu oder ab, am Ende wird
/// abgerechnet. Nur Bestaetigtes und Abgerechnetes steht als Verpflichtung im
/// Kalender; eine Anfrage erscheint dort als Vorbehalt, damit niemand denselben
/// Abend doppelt verspricht.
/// </remarks>
public enum TourStopStatus
{
    /// <summary>Der Gastgeber hat gefragt, mehr nicht.</summary>
    Inquiry = 1,

    /// <summary>Das Haus hat ein Angebot gemacht und wartet.</summary>
    Offered = 2,

    /// <summary>Beide Seiten haben zugesagt.</summary>
    Confirmed = 3,

    /// <summary>Kommt nicht zustande. Bleibt stehen, damit die Absage nachlesbar ist.</summary>
    Cancelled = 4,

    /// <summary>Gespielt und abgerechnet.</summary>
    Settled = 5,
}

public static class TourStopStatuses
{
    public static readonly TourStopStatus[] All = Enum.GetValues<TourStopStatus>();

    public static string Label(this TourStopStatus status) => status switch
    {
        TourStopStatus.Inquiry => "Anfrage",
        TourStopStatus.Offered => "Angeboten",
        TourStopStatus.Confirmed => "Bestätigt",
        TourStopStatus.Cancelled => "Abgesagt",
        TourStopStatus.Settled => "Abgerechnet",
        _ => status.ToString(),
    };

    /// <summary>Ob das Gastspiel stattfindet oder stattgefunden hat.</summary>
    public static bool IsBinding(this TourStopStatus status) =>
        status is TourStopStatus.Confirmed or TourStopStatus.Settled;

    /// <summary>
    /// Ob das Gastspiel noch im Kalender steht. Abgesagtes verschwindet dort,
    /// bleibt aber in der Liste der Tournee.
    /// </summary>
    public static bool AppearsInCalendar(this TourStopStatus status) =>
        status is not TourStopStatus.Cancelled;
}

/// <summary>
/// Ein Gastspiel: die Produktion einer Tournee an einer Spielstaette, mit
/// allem, was den Ort betrifft.
/// </summary>
/// <remarks>
/// Die Spielstaette ist ein Kontakt mit der Rolle „Spielstaette", die
/// Ansprechperson ein Kontakt daneben. Nicht freier Text: Dieselbe Buehne
/// bekommt naechste Spielzeit wieder ein Angebot, und ihre Anschrift soll
/// dann noch stimmen.
///
/// Die Gage ist eine Zahl, kein Beleg. Erst wenn das Haus die Rechnung
/// schreibt, entsteht in den Finanzen ein Beleg, und <see cref="VoucherId"/>
/// zeigt darauf -- so bleibt nachvollziehbar, ob eine zugesagte Gage schon
/// in Rechnung gestellt ist.
/// </remarks>
public class TourStop : Entity
{
    public Guid TourId { get; set; }

    public Tour Tour { get; set; } = null!;

    /// <summary>Wo gespielt wird. Ein Kontakt mit der Rolle Spielstaette.</summary>
    public required Guid VenueId { get; set; }

    public Contact? Venue { get; set; }

    /// <summary>Mit wem dort gesprochen wird.</summary>
    public Guid? ContactPersonId { get; set; }

    public Contact? ContactPerson { get; set; }

    public TourStopStatus Status { get; set; } = TourStopStatus.Inquiry;

    /// <summary>
    /// Anreise und Abreise als Tage: Die Reise erscheint im Kalender als Balken
    /// ueber den Zeitraum, die Vorstellungen als Zeitpunkte darin.
    /// </summary>
    public DateOnly? ArrivalDate { get; set; }

    public DateOnly? DepartureDate { get; set; }

    /// <summary>Vereinbarte Gage in Euro, brutto. Null heisst: noch offen.</summary>
    public decimal? Fee { get; set; }

    /// <summary>Ob der Gastgeber die Reisekosten traegt -- sonst das Haus.</summary>
    public bool HostCoversTravel { get; set; }

    /// <summary>Ob der Gastgeber die Unterkunft stellt.</summary>
    public bool HostCoversAccommodation { get; set; }

    /// <summary>Der Beleg der Gagenrechnung, sobald sie geschrieben ist.</summary>
    public Guid? VoucherId { get; set; }

    public Voucher? Voucher { get; set; }

    public string? Notes { get; set; }

    public List<TourPerformance> Performances { get; set; } = [];

    /// <summary>Wie das Gastspiel im Kalender und in Listen heisst.</summary>
    public string DisplayTitle(string tourTitle) =>
        Venue is { } venue ? $"{tourTitle} · {venue.Name}" : tourTitle;

    /// <summary>Der Ort, wie ihn der Kalender zeigt: Spielstaette, Stadt.</summary>
    public string? Location => Venue switch
    {
        null => null,
        { City: { Length: > 0 } city } venue => $"{venue.Name}, {city}",
        { } venue => venue.Name,
    };

    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];

        if (VenueId == Guid.Empty)
        {
            problems.Add("Ein Gastspiel braucht eine Spielstätte.");
        }

        if (ArrivalDate is { } arrival && DepartureDate is { } departure && departure < arrival)
        {
            problems.Add("Die Abreise liegt vor der Anreise.");
        }

        if (Fee is < 0)
        {
            problems.Add("Eine Gage ist nicht negativ.");
        }

        foreach (TourPerformance performance in Performances)
        {
            problems.AddRange(performance.Validate(this));
        }

        if (Performances.Select(p => p.StartsAt).Distinct().Count() != Performances.Count)
        {
            problems.Add("Zwei Vorstellungen beginnen zur selben Zeit.");
        }

        return problems;
    }
}

/// <summary>
/// Eine Vorstellung innerhalb eines Gastspiels. Eigene Zeile, weil jede im
/// Kalender zu ihrer Zeit steht -- drei Abende in Hamburg sind drei Eintraege.
/// </summary>
public class TourPerformance : Entity
{
    public Guid StopId { get; set; }

    public TourStop Stop { get; set; } = null!;

    public required DateTimeOffset StartsAt { get; set; }

    public required DateTimeOffset EndsAt { get; set; }

    public string? Note { get; set; }

    public IReadOnlyList<string> Validate(TourStop stop)
    {
        List<string> problems = [];

        if (EndsAt <= StartsAt)
        {
            problems.Add("Eine Vorstellung endet nach ihrem Beginn.");
        }

        // Die Reise umschliesst die Vorstellungen. Eine Vorstellung vor der
        // Anreise ist kein Sonderfall, sondern ein Tippfehler.
        DateOnly day = DateOnly.FromDateTime(StartsAt.UtcDateTime);
        if (stop.ArrivalDate is { } arrival && day < arrival.AddDays(-1))
        {
            problems.Add($"Die Vorstellung am {day:dd.MM.yyyy} liegt vor der Anreise.");
        }

        if (stop.DepartureDate is { } departure && day > departure.AddDays(1))
        {
            problems.Add($"Die Vorstellung am {day:dd.MM.yyyy} liegt nach der Abreise.");
        }

        return problems;
    }
}
