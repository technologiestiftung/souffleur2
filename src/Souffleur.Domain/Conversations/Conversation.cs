using Souffleur.Domain.Common;

namespace Souffleur.Domain.Conversations;

/// <summary>
/// Ein Gespräch. Entweder für sich -- ein Kanal des Hauses oder eine
/// Direktnachricht -- oder an einen Gegenstand geheftet.
/// </summary>
/// <remarks>
/// Der Chat ist ein eigenes Modul und keine Beigabe zu den Aufgaben: Die Frage
/// „ist diese Rechnung schon freigegeben?" gehoert an die Rechnung, nicht in
/// einen Aufgabenraum. Deshalb kann jede Unterhaltung an einem Gegenstand
/// haengen -- an einer Aufgabe ebenso wie an einem Beleg, einer Buchung oder
/// einem Kontakt.
///
/// Die Bindung ist bewusst lose (<see cref="SubjectType"/> und
/// <see cref="SubjectId"/>) statt als Fremdschluessel je Modul. Der Preis ist,
/// dass die Datenbank sie nicht prueft; der Gewinn ist, dass ein neues Modul
/// einen Chat bekommt, ohne dass hier etwas zu aendern waere. Tragbar, weil in
/// souffleur fast nichts geloescht wird -- Buchungen nie, Kontakte werden
/// stillgelegt.
/// </remarks>
public class Conversation : Entity
{
    public required ConversationKind Kind { get; set; }

    /// <summary>
    /// Name des Kanals. Bei einer Unterhaltung an einem Gegenstand leer --
    /// dort steht der Gegenstand selbst in der Überschrift.
    /// </summary>
    public string? Name { get; set; }

    public string? Topic { get; set; }

    /// <summary>An welcher Art von Gegenstand die Unterhaltung hängt.</summary>
    public ConversationSubject SubjectType { get; set; } = ConversationSubject.None;

    public Guid? SubjectId { get; set; }

    /// <summary>
    /// Ein geschlossener Kanal ist nur für seine Teilnehmer sichtbar. Offene
    /// Kanäle sieht jeder im Haus.
    /// </summary>
    public bool IsPrivate { get; set; }

    /// <summary>
    /// Archiviert heißt: bleibt lesbar, nimmt aber nichts Neues mehr auf.
    /// Das ist der Regelweg. Gelöscht wird nur ein Kanal, und nur von dem,
    /// der ihn angelegt hat, oder der Verwaltung -- Direktnachrichten und
    /// Unterhaltungen an einem Gegenstand bleiben, sie können Teil der
    /// Nachvollziehbarkeit sein.
    /// </summary>
    public DateTimeOffset? ArchivedAt { get; set; }

    public DateTimeOffset? LastMessageAt { get; set; }

    public List<ConversationParticipant> Participants { get; set; } = [];

    public List<Message> Messages { get; set; } = [];

    public bool IsArchived => ArchivedAt is not null;

    public bool IsAttached => SubjectType != ConversationSubject.None && SubjectId is not null;

    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];

        if (Kind == ConversationKind.Channel && string.IsNullOrWhiteSpace(Name))
        {
            problems.Add("Ein Kanal braucht einen Namen.");
        }

        if (Kind == ConversationKind.Attached && !IsAttached)
        {
            problems.Add("Eine angeheftete Unterhaltung braucht einen Gegenstand.");
        }

        if (Kind != ConversationKind.Attached && IsAttached)
        {
            problems.Add("Nur angeheftete Unterhaltungen hängen an einem Gegenstand.");
        }

        if (Kind == ConversationKind.Direct && Participants.Count < 2)
        {
            problems.Add("Eine Direktnachricht braucht mindestens zwei Beteiligte.");
        }

        return problems;
    }
}

public enum ConversationKind
{
    /// <summary>Offener oder geschlossener Kanal des Hauses.</summary>
    Channel = 1,

    /// <summary>Unterhaltung zwischen benannten Personen.</summary>
    Direct = 2,

    /// <summary>Hängt an einem Gegenstand und erbt dessen Sichtbarkeit.</summary>
    Attached = 3,
}

/// <summary>
/// Woran eine Unterhaltung haengen kann. Erweitert sich mit den Modulen;
/// die Werte bleiben stabil, weil sie in der Datenbank stehen.
/// </summary>
public enum ConversationSubject
{
    None = 0,
    Task = 1,
    Voucher = 2,
    JournalEntry = 3,
    Contact = 4,
    Document = 5,
    Production = 6,
    Shift = 7,
    Item = 8,
}

public static class ConversationSubjects
{
    public static string Label(this ConversationSubject subject) => subject switch
    {
        ConversationSubject.Task => "Aufgabe",
        ConversationSubject.Voucher => "Beleg",
        ConversationSubject.JournalEntry => "Buchung",
        ConversationSubject.Contact => "Kontakt",
        ConversationSubject.Document => "Dokument",
        ConversationSubject.Production => "Produktion",
        ConversationSubject.Shift => "Schicht",
        ConversationSubject.Item => "Gegenstand",
        _ => string.Empty,
    };
}
