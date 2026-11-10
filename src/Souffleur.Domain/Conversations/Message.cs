using Souffleur.Domain.Common;
using Souffleur.Domain.Identity;

namespace Souffleur.Domain.Conversations;

/// <summary>
/// Eine Nachricht in einer Unterhaltung.
/// </summary>
/// <remarks>
/// Nachrichten sind personenbezogene Daten. Geloescht wird deshalb nicht hart:
/// Der Text verschwindet, die Zeile bleibt mit dem Hinweis stehen, dass hier
/// etwas stand. Sonst waere ein Gespraech nachtraeglich umschreibbar, und
/// niemand koennte sagen, ob eine Antwort je eine Frage hatte.
/// </remarks>
public class Message : Entity
{
    public Guid ConversationId { get; set; }

    public Conversation Conversation { get; set; } = null!;

    public Guid AuthorId { get; set; }

    public User Author { get; set; } = null!;

    public required string Text { get; set; }

    public DateTimeOffset? EditedAt { get; set; }

    /// <summary>Gesetzt heißt: zurückgezogen. Der Text ist dann leer.</summary>
    public DateTimeOffset? RetractedAt { get; set; }

    public Guid? RetractedBy { get; set; }

    /// <summary>Antwort auf eine andere Nachricht -- der Faden im Gespräch.</summary>
    public Guid? InReplyToId { get; set; }

    public Message? InReplyTo { get; set; }

    public List<MessageMention> Mentions { get; set; } = [];

    public bool IsRetracted => RetractedAt is not null;

    /// <summary>
    /// Zieht die Nachricht zurück. Nur die eigene, und nur was die Verwaltung
    /// nicht ohnehin darf.
    /// </summary>
    public void Retract(DateTimeOffset at, Guid by)
    {
        if (IsRetracted)
        {
            return;
        }

        Text = string.Empty;
        RetractedAt = at;
        RetractedBy = by;
        Mentions.Clear();
    }

    public void Edit(string text, DateTimeOffset at)
    {
        if (IsRetracted)
        {
            throw new InvalidOperationException("Eine zurückgezogene Nachricht lässt sich nicht ändern.");
        }

        Text = text;
        EditedAt = at;
    }

    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];

        if (string.IsNullOrWhiteSpace(Text))
        {
            problems.Add("Eine leere Nachricht wird nicht gesendet.");
        }

        if (Text.Length > 8000)
        {
            problems.Add("Die Nachricht ist zu lang; 8000 Zeichen sind die Grenze.");
        }

        return problems;
    }
}

/// <summary>
/// Wer in einer Nachricht angesprochen wurde. Eigene Zeile, damit sich
/// „was betrifft mich?" beantworten lässt, ohne jede Nachricht zu lesen.
/// </summary>
public class MessageMention : Entity
{
    public Guid MessageId { get; set; }

    public Message Message { get; set; } = null!;

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public DateTimeOffset? ReadAt { get; set; }
}
