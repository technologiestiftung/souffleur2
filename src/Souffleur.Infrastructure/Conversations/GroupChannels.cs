using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Conversations;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Conversations;

/// <summary>
/// Haelt die Kanaele einer Gruppe auf dem Stand ihrer Mitglieder.
/// </summary>
/// <remarks>
/// Statisch und nur auf dem Kontext, weil zwei Stellen es brauchen, die
/// einander nicht kennen sollen: der Chat beim Anlegen eines Kanals und die
/// Verwaltung der Zugaenge, wenn sich eine Gruppe aendert.
///
/// Wer geht, bekommt <c>LeftAt</c> und keinen geloeschten Eintrag: Was er
/// geschrieben hat, bleibt seins, und kommt er zurueck, gilt sein
/// Lesestand weiter.
/// </remarks>
public static class GroupChannels
{
    public static async Task SyncAsync(
        SouffleurDbContext context,
        Guid groupId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        List<Conversation> channels = await context.Conversations
            .Include(c => c.Participants)
            .Where(c => c.GroupId == groupId && c.ArchivedAt == null)
            .ToListAsync(cancellationToken);
        if (channels.Count == 0)
        {
            return;
        }

        HashSet<Guid> members = [.. await context.GroupMembers
            .Where(m => m.GroupId == groupId)
            .Select(m => m.UserId)
            .ToListAsync(cancellationToken)];

        foreach (Conversation channel in channels)
        {
            foreach (ConversationParticipant participant in channel.Participants)
            {
                bool belongs = members.Contains(participant.UserId);
                if (belongs && participant.LeftAt is not null)
                {
                    participant.LeftAt = null;
                }
                else if (!belongs && participant.LeftAt is null)
                {
                    participant.LeftAt = now;
                }
            }

            foreach (Guid userId in members.Except(channel.Participants.Select(p => p.UserId)))
            {
                var participant = new ConversationParticipant
                {
                    ConversationId = channel.Id,
                    UserId = userId,
                    JoinedAt = now,
                    ReadUpTo = now,
                };
                context.ConversationParticipants.Add(participant);
                channel.Participants.Add(participant);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
