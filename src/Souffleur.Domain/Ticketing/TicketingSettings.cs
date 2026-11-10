using Souffleur.Domain.Common;

namespace Souffleur.Domain.Ticketing;

/// <summary>
/// Der Vorverkauf eines Hauses: welcher, und wie man ihn erreicht.
/// </summary>
/// <remarks>
/// Ein Datensatz je Installation. Jedes Haus entscheidet selbst, ob es seinen
/// Vorverkauf anbindet -- manche verkaufen gar nicht selbst, andere nur ueber
/// die Abendkasse. Deshalb ist <see cref="TicketingProvider.None"/> der
/// Ausgangszustand und kein Fehler.
/// </remarks>
public class TicketingSettings : Entity
{
    public TicketingProvider Provider { get; set; } = TicketingProvider.None;

    /// <summary>Die Adresse der Instanz, etwa https://pretix.eu.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Der Veranstalter-Kurzname in pretix.</summary>
    public string? Organizer { get; set; }

    /// <summary>
    /// Das API-Token. Lesen genuegt: souffleur schreibt nichts in den
    /// Vorverkauf zurueck. Das Token verlaesst den Server nie wieder -- die
    /// Oberflaeche erfaehrt nur, ob eines hinterlegt ist.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// Wie lange eine Antwort wiederverwendet wird. Eine gehostete Instanz
    /// hat Abfragegrenzen, und jede Seite loest sonst mehrere Abfragen je
    /// Vorstellung aus.
    /// </summary>
    public int CacheSeconds { get; set; } = 60;

    /// <summary>Wann die Verbindung zuletzt geantwortet hat.</summary>
    public DateTimeOffset? LastCheckedAt { get; set; }

    /// <summary>Woran sie zuletzt gescheitert ist. Null heisst: sie steht.</summary>
    public string? LastProblem { get; set; }

    public bool IsConfigured =>
        Provider != TicketingProvider.None
        && !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(Organizer)
        && !string.IsNullOrWhiteSpace(Token);

    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];

        if (Provider == TicketingProvider.None)
        {
            return problems;
        }

        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            problems.Add("Ohne Adresse der pretix-Instanz gibt es nichts abzufragen.");
        }
        else if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out Uri? parsed)
                 || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            problems.Add($"„{BaseUrl}“ ist keine vollständige Adresse (erwartet wird https://…).");
        }

        if (string.IsNullOrWhiteSpace(Organizer))
        {
            problems.Add("Der Veranstalter-Kurzname fehlt — in pretix steht er in der Adresse.");
        }

        if (string.IsNullOrWhiteSpace(Token))
        {
            problems.Add("Ohne Token lässt sich die Schnittstelle nicht aufrufen.");
        }

        if (CacheSeconds is < 0 or > 3600)
        {
            problems.Add("Die Zwischenspeicherung liegt zwischen 0 Sekunden und einer Stunde.");
        }

        return problems;
    }
}

public enum TicketingProvider
{
    /// <summary>Kein angebundener Vorverkauf. Das Modul zeigt dann nichts an.</summary>
    None = 0,

    /// <summary>pretix, selbst betrieben oder gehostet auf pretix.eu.</summary>
    Pretix = 1,
}
