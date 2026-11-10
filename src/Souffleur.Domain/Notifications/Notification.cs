using Souffleur.Domain.Common;

namespace Souffleur.Domain.Notifications;

/// <summary>Wofuer jemand Bescheid bekommt.</summary>
/// <remarks>
/// Die Arten sind zugleich die Zeilen der Einstellungsseite: Je Art waehlt
/// jede Person, ob dazu auch eine E-Mail kommt. In der Glocke steht alles.
/// </remarks>
public enum NotificationKind
{
    /// <summary>Jemand hat mich oder eine meiner Gruppen im Chat erwaehnt.</summary>
    Mention = 1,

    /// <summary>Eine Aufgabe ging an mich oder meine Gruppe, oder ihre Frist ist morgen.</summary>
    Task = 2,

    /// <summary>Ich wurde eingeteilt, ausgetragen, oder mein Termin hat sich verschoben.</summary>
    Duty = 3,

    /// <summary>Ein Dokument wurde fuer eine meiner Gruppen abgelegt.</summary>
    Document = 4,

    /// <summary>
    /// Ein Antrag auf Abwesenheit: neu (an die, die entscheiden), entschieden
    /// (an die, die ihn gestellt hat), eine Krankmeldung.
    /// </summary>
    Absence = 5,
}

/// <summary>Ob und wann zu einer Art eine E-Mail kommt.</summary>
public enum EmailMode
{
    /// <summary>Nur die Glocke. Die Vorgabe -- niemand bekommt ungefragt Post.</summary>
    Off = 0,

    /// <summary>Gleich nach dem Ereignis, eine Mail je Benachrichtigung.</summary>
    Immediate = 1,

    /// <summary>Einmal am Morgen, alles Ungelesene vom Vortag in einer Mail.</summary>
    Daily = 2,
}

/// <summary>
/// Eine Benachrichtigung in der Glocke einer Person.
/// </summary>
/// <remarks>
/// Je Person eine Zeile, auch wenn dasselbe Ereignis zehn Leute betrifft:
/// Gelesen ist etwas fuer jeden einzeln.
///
/// Der Text wird beim Entstehen festgeschrieben und nicht beim Anzeigen
/// zusammengesetzt. Wird die Aufgabe spaeter umbenannt, steht in der Glocke
/// weiter, was damals passiert ist -- das ist die ehrlichere Auskunft.
/// </remarks>
public class Notification : Entity
{
    public required Guid UserId { get; set; }

    public required NotificationKind Kind { get; set; }

    /// <summary>Eine Zeile, etwa „Anna hat dich in #technik erwähnt".</summary>
    public required string Title { get; set; }

    /// <summary>Was dazugehoert -- der Anfang der Nachricht, der Name der Aufgabe.</summary>
    public string? Body { get; set; }

    /// <summary>Wohin ein Klick fuehrt, als Pfad in der Oberflaeche.</summary>
    public string? Link { get; set; }

    public DateTimeOffset? ReadAt { get; set; }

    /// <summary>
    /// Wann die Mail dazu raus ist. Zugleich die Sperre gegen doppelten
    /// Versand: Was hier einen Wert hat, geht nicht noch einmal.
    /// </summary>
    public DateTimeOffset? EmailedAt { get; set; }

    public bool IsRead => ReadAt is not null;
}

public static class NotificationKinds
{
    public static readonly NotificationKind[] All = Enum.GetValues<NotificationKind>();

    public static string Label(this NotificationKind kind) => kind switch
    {
        NotificationKind.Mention => "Erwähnungen im Chat",
        NotificationKind.Task => "Aufgaben an mich oder meine Gruppen",
        NotificationKind.Duty => "Dienste: eingeteilt, ausgetragen, verschoben",
        NotificationKind.Document => "Neue Dokumente für meine Gruppen",
        NotificationKind.Absence => "Urlaubsanträge und Krankmeldungen",
        _ => kind.ToString(),
    };

    /// <summary>Unter diesem Schluessel steht die E-Mail-Wahl in den Einstellungen der Person.</summary>
    public static string PreferenceKey(this NotificationKind kind) => $"benachrichtigung:{kind.ToString().ToLowerInvariant()}";
}
