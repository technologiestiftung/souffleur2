using Souffleur.Domain.Common;

namespace Souffleur.Domain.Bookmarks;

/// <summary>
/// Woran ein Lesezeichen haengen kann.
/// </summary>
/// <remarks>
/// Dieselbe Bauart wie <c>DocumentTarget</c> und <c>ConversationSubject</c>:
/// Art und Id statt eines Fremdschluessels je Ziel. Ein neues Ziel ist ein
/// Enum-Wert und eine Zeile in der Oberflaeche, keine Migration. Die Zahlen
/// decken sich, wo es dasselbe Ziel gibt, mit <c>DocumentTarget</c> -- wer
/// beides liest, soll nicht zwei Tabellen im Kopf brauchen.
/// </remarks>
public enum BookmarkTarget
{
    Contact = 1,
    Task = 2,
    Event = 3,
    JournalEntry = 4,
    Voucher = 5,
    TourStop = 6,
    Production = 7,
    Document = 8,
    Tour = 9,
}

/// <summary>
/// Eine Adresse im Netz, an einen Gegenstand des Hauses geheftet: das
/// Rechercheergebnis zur Produktion, das Video zur Aufgabe, die Website der
/// Spielstaette am Kontakt.
/// </summary>
/// <remarks>
/// Ein eigener Begriff neben dem Dokument: Ein Dokument liegt in der Ablage
/// und gehoert dem Haus; ein Lesezeichen zeigt nur irgendwohin und kann
/// morgen ins Leere fuehren. Deshalb keine Pruefung der Erreichbarkeit und
/// kein Herunterladen -- nur die Adresse, ein Stichwort und die Reihenfolge.
///
/// Erlaubt sind http und https. Alles andere (javascript:, file:) waere ein
/// Weg, aus einem harmlosen Lesezeichen etwas anderes zu machen.
/// </remarks>
public class Bookmark : Entity
{
    public required BookmarkTarget Target { get; set; }

    public required Guid TargetId { get; set; }

    public required string Url { get; set; }

    /// <summary>Das Stichwort, unter dem es in der Liste steht. Fehlt es, zeigt die Oberflaeche den Host.</summary>
    public string? Label { get; set; }

    public int SortOrder { get; set; }

    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];

        if (!Uri.TryCreate(Url, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            problems.Add("Ein Lesezeichen braucht eine Adresse, die mit http:// oder https:// beginnt.");
        }

        if (Url.Length > 2000)
        {
            problems.Add("Die Adresse ist länger als 2000 Zeichen.");
        }

        if (Label is { Length: > 200 })
        {
            problems.Add("Das Stichwort ist länger als 200 Zeichen.");
        }

        return problems;
    }

    /// <summary>
    /// Ergaenzt ein fehlendes https:// -- wer „youtube.com/…" einfuegt,
    /// meint die Website, nicht einen Tippfehler.
    /// </summary>
    public static string Normalize(string url)
    {
        string trimmed = url.Trim();
        return trimmed.Length > 0 && !trimmed.Contains("://") ? $"https://{trimmed}" : trimmed;
    }
}
