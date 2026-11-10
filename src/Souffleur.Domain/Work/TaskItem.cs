using Souffleur.Domain.Accounting;
using Souffleur.Domain.Catalogs;
using Souffleur.Domain.Common;
using Souffleur.Domain.Contacts;
using Souffleur.Domain.Identity;
using Souffleur.Domain.Productions;

namespace Souffleur.Domain.Work;

/// <summary>
/// Eine Aufgabe: etwas, das jemand bis zu einem Zeitpunkt tun soll.
/// </summary>
/// <remarks>
/// Heißt <c>TaskItem</c> und nicht <c>Task</c>, weil der Name in .NET schon
/// vergeben ist -- ein Feld <c>Task</c> neben jedem <c>await</c> wäre eine
/// Falle für jeden, der später hier liest.
///
/// Die Zuständigkeit kommt aus dem Adressbuch und nicht aus den Benutzerkonten:
/// Vieles im Kulturbetrieb erledigt, wer kein Konto hat -- die freie
/// Ausstatterin, die Agentur, der Steuerberater. Ein Konto hat, wer souffleur
/// benutzt; eine Aufgabe hat, wer sie übernimmt.
/// </remarks>
public class TaskItem : Entity
{
    public required string Title { get; set; }

    public string? Description { get; set; }

    /// <summary>Wonach das Haus sortiert -- ein Katalog (ADR-0005).</summary>
    public Guid? CategoryId { get; set; }

    public CatalogEntry? Category { get; set; }

    public TaskState State { get; set; } = TaskState.Open;

    /// <summary>Wer sie übernommen hat.</summary>
    public Guid? AssigneeId { get; set; }

    public Contact? Assignee { get; set; }

    /// <summary>
    /// Welche Gruppe zuständig ist -- „die Technik" statt einer Person.
    /// </summary>
    /// <remarks>
    /// Neben <see cref="Assignee"/>, nicht statt: Eine Gruppe bekommt die
    /// Aufgabe, und wer sie dort übernimmt, trägt sich als Zuständige ein.
    /// Beides zusammen ist der Normalfall, nicht der Widerspruch.
    /// </remarks>
    public Guid? GroupId { get; set; }

    public Group? Group { get; set; }

    /// <summary>
    /// Zu welcher Produktion die Aufgabe gehoert -- „Buehnenbild abnehmen"
    /// ist eine Aufgabe von Hamlet. Die Produktion liest ihre Aufgaben
    /// hierueber (ADR-0011); faellt sie weg, bleibt die Aufgabe ohne Klammer.
    /// </summary>
    public Guid? ProductionId { get; set; }

    public Production? Production { get; set; }

    /// <summary>
    /// An welcher Kostenstelle die Aufgabe haengt -- „Marketing Hamlet"
    /// steht dann neben den Buchungen, die dorthin gehen. Eine Verknuepfung,
    /// kein Buchungsvorgang: Die Aufgabe kostet nichts, sie gehoert nur wohin.
    /// </summary>
    public Guid? CostCenterId { get; set; }

    public CostCenter? CostCenter { get; set; }

    /// <summary>
    /// Beteiligte und verknuepfte Kontakte -- siehe <see cref="TaskContact"/>.
    /// Der eine Zustaendige steht in <see cref="Assignee"/>.
    /// </summary>
    public List<TaskContact> Contacts { get; set; } = [];

    public IEnumerable<TaskContact> Participants =>
        Contacts.Where(c => c.Kind == TaskContactKind.Participant);

    public IEnumerable<TaskContact> RelatedContacts =>
        Contacts.Where(c => c.Kind == TaskContactKind.Related);

    /// <summary>
    /// Bis wann. Erscheint im Kalender als Frist -- gelesen, nicht kopiert
    /// (ADR-0006).
    /// </summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>
    /// Ab wann daran gearbeitet wird. Zusammen mit <see cref="DueDate"/> die
    /// Grundlage für den Zeitstrahl: Ein Vorgang hat eine Dauer, nicht nur
    /// einen Stichtag.
    /// </summary>
    public DateOnly? StartDate { get; set; }

    /// <summary>
    /// Übergeordnete Aufgabe. So wird aus einer Aufgabe eine Checkliste,
    /// ohne dafür ein zweites Modell zu brauchen.
    /// </summary>
    public Guid? ParentId { get; set; }

    public TaskItem? Parent { get; set; }

    public List<TaskItem> Children { get; set; } = [];

    /// <summary>Worauf diese Aufgabe wartet.</summary>
    public List<TaskDependency> DependsOn { get; set; } = [];

    /// <summary>Was auf diese Aufgabe wartet.</summary>
    public List<TaskDependency> Blocks { get; set; } = [];

    public int SortOrder { get; set; }

    /// <summary>Wann sie abgeschlossen wurde. Setzt der Dienst, nicht der Aufrufer.</summary>
    public DateTimeOffset? ClosedAt { get; set; }

    public bool IsOpen => State.IsOpen();

    /// <summary>
    /// Ob die Aufgabe auf dem Zeitstrahl einen Balken hat. Ohne Anfang und
    /// ohne Frist gibt es nichts aufzutragen.
    /// </summary>
    public bool IsScheduled => StartDate is not null || DueDate is not null;

    /// <summary>
    /// Der Balken: von wann bis wann. Fehlt der Anfang, ist es ein Stichtag --
    /// dann sind beide gleich, und die Ansicht zeichnet eine Raute statt eines
    /// Balkens.
    /// </summary>
    public (DateOnly From, DateOnly To)? Span =>
        (StartDate, DueDate) switch
        {
            ({ } start, { } due) => (start, due),
            ({ } start, null) => (start, start),
            (null, { } due) => (due, due),
            _ => null,
        };

    /// <summary>
    /// Ob die Frist verstrichen ist. Erledigtes ist nie überfällig -- auch
    /// nicht, wenn es zu spät erledigt wurde.
    /// </summary>
    public bool IsOverdue(DateOnly today) =>
        IsOpen && DueDate is { } due && due < today;

    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];

        if (string.IsNullOrWhiteSpace(Title))
        {
            problems.Add("Ein Titel ist nötig.");
        }

        if (StartDate is { } start && DueDate is { } due && due < start)
        {
            problems.Add("Die Frist liegt vor dem Beginn.");
        }

        if (ParentId == Id)
        {
            problems.Add("Eine Aufgabe kann sich nicht selbst übergeordnet sein.");
        }

        return problems;
    }
}
