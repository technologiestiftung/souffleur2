using Souffleur.Domain.Common;

namespace Souffleur.Domain.Calendar;

/// <summary>
/// Eine Adresse, unter der ein Ausschnitt des Kalenders als iCalendar
/// abonniert werden kann.
/// </summary>
/// <remarks>
/// Kein Export, sondern ein Abonnement: Die Kalenderanwendung fragt die
/// Adresse regelmäßig ab und übernimmt, was sich geändert hat. Eine
/// heruntergeladene Datei wäre in dem Moment veraltet, in dem die Disposition
/// etwas verschiebt.
///
/// Weil Kalenderanwendungen sich nicht anmelden können, steckt die Berechtigung
/// im <see cref="Token"/>: Wer die Adresse hat, sieht den Ausschnitt. Deshalb
/// lässt sie sich zurückziehen (neues Token) und abschalten.
/// </remarks>
public class CalendarSubscription : Entity
{
    /// <summary>Wofür das Abonnement da ist -- „Spielplan“, „Großer Saal“.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// Der Schlüssel in der Adresse. Zufällig, lang genug zum Nichtraten.
    /// </summary>
    /// <remarks>
    /// Im Klartext gespeichert und nicht als Streuwert: Wer die Datenbank
    /// lesen kann, liest ohnehin die Termine, die das Token schützt. Dafür
    /// lässt sich die Adresse später noch einmal anzeigen -- sonst wäre sie
    /// nach dem Anlegen für immer verloren.
    /// </remarks>
    public required string Token { get; set; }

    /// <summary>Wer das Abonnement angelegt hat.</summary>
    public Guid? OwnerId { get; set; }

    /// <summary>Leer heißt: alle Kategorien.</summary>
    public List<Guid> CategoryIds { get; set; } = [];

    /// <summary>Leer heißt: alles, unabhängig davon, was es belegt.</summary>
    public List<Guid> ResourceIds { get; set; } = [];

    /// <summary>
    /// Nur Termine, bei denen diese Leute dabei sind. Leer heißt: alle.
    /// </summary>
    /// <remarks>
    /// Damit wird aus dem Spielplan ein Dienstplan: Ein Abonnement auf die
    /// eigene Person zeigt im Telefon genau die Termine, zu denen man kommen
    /// muss -- und nichts sonst.
    /// </remarks>
    public List<Guid> ContactIds { get; set; } = [];

    /// <summary>
    /// Ob abgesagte Termine mitgehen -- dann als abgesagt gekennzeichnet,
    /// nicht einfach fehlend.
    /// </summary>
    public bool IncludeCancelled { get; set; } = true;

    /// <summary>Abgeschaltet heißt: Die Adresse antwortet nicht mehr.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Wann zuletzt abgerufen. Steht da, damit sichtbar wird, welche Adressen
    /// noch jemand benutzt -- und welche man zurückziehen kann.
    /// </summary>
    public DateTimeOffset? LastFetchedAt { get; set; }

    public int FetchCount { get; set; }

    /// <summary>Die Länge des Tokens in Bytes vor der Kodierung.</summary>
    public const int TokenBytes = 24;
}
