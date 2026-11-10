using Souffleur.Domain.Common;
using Souffleur.Domain.Identity;

namespace Souffleur.Domain.Conversations;

/// <summary>
/// Wer an einer Unterhaltung teilnimmt -- und bis wohin gelesen hat.
/// </summary>
/// <remarks>
/// Bei offenen Kanaelen steht hier nur, wer beigetreten ist; lesen darf dort
/// jeder im Haus. Bei Direktnachrichten und geschlossenen Kanaelen entscheidet
/// diese Liste, wer ueberhaupt hineinsieht.
/// </remarks>
public class ConversationParticipant : Entity
{
    public Guid ConversationId { get; set; }

    public Conversation Conversation { get; set; } = null!;

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public DateTimeOffset JoinedAt { get; set; }

    /// <summary>Bis zu welchem Zeitpunkt gelesen wurde -- daraus entsteht die Zahl an der Glocke.</summary>
    public DateTimeOffset? ReadUpTo { get; set; }

    /// <summary>Stumm geschaltet: taucht nicht in Benachrichtigungen auf.</summary>
    public bool IsMuted { get; set; }

    /// <summary>Verlassen heißt: bleibt in der Liste, bekommt aber nichts mehr.</summary>
    public DateTimeOffset? LeftAt { get; set; }

    public bool IsActive => LeftAt is null;
}
