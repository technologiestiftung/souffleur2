using Souffleur.Domain.Common;
using Souffleur.Domain.Contacts;

namespace Souffleur.Domain.Work;

/// <summary>
/// In welcher Weise ein Kontakt an einer Aufgabe haengt -- ausser als der
/// eine Zustaendige, der sein eigenes Feld hat.
/// </summary>
public enum TaskContactKind
{
    /// <summary>
    /// Arbeitet mit, ohne die Aufgabe zu tragen: die Regieassistenz an der
    /// Probenplanung, der Techniker an der Abnahme. Zaehlt im Kalender wie
    /// eine Beteiligung und bekommt die Glocke.
    /// </summary>
    Participant = 1,

    /// <summary>
    /// Kommt darin vor, tut aber nichts: die Spielstaette, um die es geht,
    /// die Agentur, mit der verhandelt wird. Eine Verknuepfung zum
    /// Nachschlagen, keine Zustaendigkeit.
    /// </summary>
    Related = 2,
}

/// <summary>
/// Ein Kontakt an einer Aufgabe -- als Beteiligter oder als Verknuepfung.
/// </summary>
/// <remarks>
/// ADR-0007 hatte „mehrere Zustaendige" bewusst weggelassen, weil „alle"
/// niemand heisst. Das bleibt: Es gibt weiterhin genau eine Person, die die
/// Aufgabe traegt. Beteiligte sind etwas anderes -- sie sollen wissen, dass
/// es die Aufgabe gibt, und auf ihrem Kalender steht die Frist. Verknuepfte
/// Kontakte sollen nicht einmal das; sie stehen da, damit man vom Bogen aus
/// zur Spielstaette oder zur Agentur findet.
///
/// Eine Tabelle fuer beides, mit <see cref="Kind"/> als Unterschied: Die
/// Oberflaeche zeigt zwei Felder, die Datenbank braucht keine zwei Tabellen
/// mit denselben drei Spalten.
/// </remarks>
public class TaskContact : Entity
{
    public required Guid TaskId { get; set; }

    public TaskItem? Task { get; set; }

    public required Guid ContactId { get; set; }

    public Contact? Contact { get; set; }

    public required TaskContactKind Kind { get; set; }
}
