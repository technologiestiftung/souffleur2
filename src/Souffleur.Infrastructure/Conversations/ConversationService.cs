using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Conversations;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Conversations;

/// <summary>
/// Unterhaltungen und Nachrichten. Einziger Weg dorthin, damit Sichtbarkeit und
/// Lesestände nicht umgangen werden.
/// </summary>
public class ConversationService(
    SouffleurDbContext context,
    TimeProvider timeProvider,
    ICurrentUser currentUser)
{
    private Guid Me => currentUser.Id ?? throw new ConversationException(["Nicht angemeldet."]);

    /// <summary>
    /// Was diese Person sehen darf: offene Kanäle und alles, woran sie
    /// beteiligt ist.
    /// </summary>
    public IQueryable<Conversation> Visible(Guid? userId = null)
    {
        Guid who = userId ?? Me;

        return context.Conversations
            .Where(c => !c.IsPrivate || c.Participants.Any(p => p.UserId == who && p.LeftAt == null));
    }

    public async Task<Conversation> CreateChannelAsync(
        string name,
        string? topic = null,
        bool isPrivate = false,
        IReadOnlyList<Guid>? participants = null,
        CancellationToken cancellationToken = default,
        Guid? groupId = null)
    {
        if (groupId is { } id && !await context.Groups.AnyAsync(g => g.Id == id && g.IsActive, cancellationToken))
        {
            throw new ConversationException(["Diese Gruppe gibt es nicht."]);
        }

        var conversation = new Conversation
        {
            Kind = ConversationKind.Channel,
            Name = name.Trim(),
            Topic = topic,
            IsPrivate = isPrivate,
            GroupId = groupId,
        };

        // Bei einem Kanal fuer eine Gruppe sind genau ihre Mitglieder drin --
        // auch wer anlegt nur, wenn er dazugehoert. Die Verwaltung legt so einen
        // Kanal fuer die Technik an, ohne selbst mitzulesen.
        AddParticipants(conversation, groupId is null ? [Me, .. participants ?? []] : []);
        Conversation created = await AddAsync(conversation, cancellationToken);

        if (groupId is { } gruppe)
        {
            await GroupChannels.SyncAsync(context, gruppe, timeProvider.GetUtcNow(), cancellationToken);
        }

        return created;
    }

    /// <summary>
    /// Holt die Unterhaltung zu einem Gegenstand, oder legt sie beim ersten
    /// Beitrag an. So entsteht kein leerer Raum an jeder Rechnung.
    /// </summary>
    public async Task<Conversation> ForSubjectAsync(
        ConversationSubject subject,
        Guid subjectId,
        CancellationToken cancellationToken = default)
    {
        Conversation? existing = await context.Conversations
            .Include(c => c.Participants)
            .FirstOrDefaultAsync(
                c => c.SubjectType == subject && c.SubjectId == subjectId, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var conversation = new Conversation
        {
            Kind = ConversationKind.Attached,
            SubjectType = subject,
            SubjectId = subjectId,
        };

        AddParticipants(conversation, [Me]);
        return await AddAsync(conversation, cancellationToken);
    }

    public async Task<Conversation> DirectAsync(
        Guid otherUserId,
        CancellationToken cancellationToken = default)
    {
        Guid me = Me;

        if (otherUserId == me)
        {
            throw new ConversationException(["Mit sich selbst redet man nicht in souffleur."]);
        }

        Conversation? existing = await context.Conversations
            .Include(c => c.Participants)
            .Where(c => c.Kind == ConversationKind.Direct)
            .Where(c => c.Participants.Count == 2)
            .Where(c => c.Participants.Any(p => p.UserId == me)
                     && c.Participants.Any(p => p.UserId == otherUserId))
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var conversation = new Conversation { Kind = ConversationKind.Direct, IsPrivate = true };
        AddParticipants(conversation, [me, otherUserId]);
        return await AddAsync(conversation, cancellationToken);
    }

    public async Task<Message> PostAsync(
        Guid conversationId,
        string text,
        Guid? inReplyToId = null,
        IReadOnlyList<Guid>? mentions = null,
        CancellationToken cancellationToken = default)
    {
        Conversation conversation = await LoadVisibleAsync(conversationId, cancellationToken);

        if (conversation.IsArchived)
        {
            throw new ConversationException(
                ["Diese Unterhaltung ist archiviert und nimmt nichts Neues mehr auf."]);
        }

        // Geantwortet wird nur auf eine Nachricht derselben Unterhaltung. Sonst
        // liesse sich ueber ihre Id eine Nachricht aus einem geschlossenen
        // Kanal anzitieren, den man gar nicht sieht.
        if (inReplyToId is { } replyTo
            && !await context.Messages.AnyAsync(
                m => m.Id == replyTo && m.ConversationId == conversationId, cancellationToken))
        {
            throw new ConversationException(
                ["Die Nachricht, auf die geantwortet wird, steht nicht in dieser Unterhaltung."]);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        var message = new Message
        {
            ConversationId = conversationId,
            AuthorId = Me,
            Text = text.Trim(),
            InReplyToId = inReplyToId,
            Mentions = [.. (mentions ?? []).Distinct().Select(id => new MessageMention { UserId = id })],
        };

        IReadOnlyList<string> problems = message.Validate();
        if (problems.Count > 0)
        {
            throw new ConversationException(problems);
        }

        // Mit Namen zurueckgeben: Die Nachricht geht von hier direkt an alle
        // offenen Oberflaechen, und dort stuende sonst eine Zeile ohne Absender.
        message.Author = await context.Users.FirstAsync(u => u.Id == message.AuthorId, cancellationToken);

        context.Messages.Add(message);
        conversation.LastMessageAt = now;

        // Wer schreibt, ist dabei -- und hat das Eigene gelesen.
        ConversationParticipant? mine = conversation.Participants.FirstOrDefault(p => p.UserId == message.AuthorId);
        if (mine is null)
        {
            Join(conversation, message.AuthorId, now);
        }
        else
        {
            mine.ReadUpTo = now;
            mine.LeftAt = null;
        }

        await context.SaveChangesAsync(cancellationToken);
        return message;
    }

    /// <summary>
    /// Den eigenen Text nachtraeglich aendern. Die Nachricht traegt danach
    /// „bearbeitet“ -- wer sie schon gelesen hat, soll sehen, dass sich etwas
    /// geaendert hat. Die Erwaehnungen bleiben, wie sie beim Senden waren: Eine
    /// Glocke, die schon geklungen hat, laesst sich nicht zuruecknehmen.
    /// </summary>
    public async Task<Message> EditAsync(Guid messageId, string text, CancellationToken cancellationToken = default)
    {
        Message message = await context.Messages
            .Include(m => m.Author)
            .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken)
            ?? throw new ConversationException(["Diese Nachricht gibt es nicht."]);

        if (message.AuthorId != Me)
        {
            throw new ConversationException(["Ändern kann nur, wer geschrieben hat."]);
        }
        if (message.IsRetracted)
        {
            throw new ConversationException(["Eine zurückgezogene Nachricht lässt sich nicht ändern."]);
        }

        Conversation conversation = await LoadVisibleAsync(message.ConversationId, cancellationToken);
        if (conversation.IsArchived)
        {
            throw new ConversationException(
                ["Diese Unterhaltung ist archiviert und nimmt nichts Neues mehr auf."]);
        }

        string before = message.Text;
        message.Edit(text.Trim(), timeProvider.GetUtcNow());
        IReadOnlyList<string> problems = message.Validate();
        if (problems.Count > 0)
        {
            message.Text = before;
            throw new ConversationException(problems);
        }

        await context.SaveChangesAsync(cancellationToken);
        return message;
    }

    public async Task RetractAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        Message message = await context.Messages
            .Include(m => m.Mentions)
            .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken)
            ?? throw new ConversationException(["Diese Nachricht gibt es nicht."]);

        if (message.AuthorId != Me)
        {
            throw new ConversationException(["Zurückziehen kann nur, wer geschrieben hat."]);
        }

        message.Retract(timeProvider.GetUtcNow(), Me);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkReadAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        Conversation conversation = await LoadVisibleAsync(conversationId, cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();

        ConversationParticipant? mine = conversation.Participants.FirstOrDefault(p => p.UserId == Me);
        if (mine is null)
        {
            Join(conversation, Me, now);
        }
        else
        {
            mine.ReadUpTo = now;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Wie viele ungelesene Nachrichten in welcher Unterhaltung warten.</summary>
    public async Task<IReadOnlyDictionary<Guid, int>> UnreadAsync(CancellationToken cancellationToken = default)
    {
        Guid me = Me;

        var rows = await context.Messages
            .Where(m => m.AuthorId != me && m.RetractedAt == null)
            .Where(m => m.Conversation.Participants.Any(p => p.UserId == me && p.LeftAt == null))
            .Select(m => new
            {
                m.ConversationId,
                ReadUpTo = m.Conversation.Participants
                    .Where(p => p.UserId == me)
                    .Select(p => p.ReadUpTo)
                    .FirstOrDefault(),
                m.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Where(r => r.ReadUpTo == null || r.CreatedAt > r.ReadUpTo)
            .GroupBy(r => r.ConversationId)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    public async Task ArchiveAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        Conversation conversation = await LoadVisibleAsync(conversationId, cancellationToken);
        conversation.ArchivedAt ??= timeProvider.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Holt eine archivierte Unterhaltung zurueck -- sie nimmt wieder Neues auf.</summary>
    public async Task UnarchiveAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        Conversation conversation = await LoadVisibleAsync(conversationId, cancellationToken);
        conversation.ArchivedAt = null;
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Loescht einen Kanal samt Nachrichten. Nur Kanaele: Direktnachrichten
    /// gehoeren zwei Personen, die eine verlaesst sie hoechstens; was an einem
    /// Gegenstand haengt, ist Teil seiner Geschichte.
    /// </summary>
    /// <remarks>
    /// Loeschen duerfen, wer den Kanal angelegt hat, und die Verwaltung
    /// (<paramref name="mayManage"/>). Alle anderen archivieren. Das ist die
    /// Ausnahme von der Regel, dass in souffleur nichts verschwindet -- ein
    /// versehentlich angelegter oder abgewegiger Kanal soll nicht fuer immer
    /// in der Liste stehen.
    /// </remarks>
    public async Task DeleteAsync(Guid conversationId, bool mayManage, CancellationToken cancellationToken = default)
    {
        Conversation conversation = await LoadVisibleAsync(conversationId, cancellationToken);

        if (conversation.Kind != ConversationKind.Channel)
        {
            throw new ConversationException(
                ["Nur Kanäle lassen sich löschen. Direktnachrichten verlässt man, Unterhaltungen an einem Gegenstand bleiben."]);
        }

        if (!mayManage && conversation.CreatedBy != Me)
        {
            throw new ConversationException(["Löschen darf nur, wer den Kanal angelegt hat, oder die Verwaltung."]);
        }

        context.Conversations.Remove(conversation);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Wer gerade dabei ist -- die Liste im Teilnehmerfenster.</summary>
    public async Task<IReadOnlyList<ConversationParticipant>> ParticipantsAsync(
        Guid conversationId, CancellationToken cancellationToken = default)
    {
        await LoadVisibleAsync(conversationId, cancellationToken);

        return await context.ConversationParticipants
            .Include(p => p.User)
            .Where(p => p.ConversationId == conversationId && p.LeftAt == null)
            .OrderBy(p => p.User.GivenName).ThenBy(p => p.User.Surname)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Nimmt Personen auf -- einzeln oder als ganze Gruppe. Eine Gruppe wird
    /// im Moment des Hinzufuegens aufgeloest: Wer spaeter in die Gruppe kommt,
    /// kommt nicht von selbst in den Kanal. Das ist einfacher zu verstehen als
    /// eine Mitgliedschaft, die sich hinter dem Ruecken aendert.
    /// </summary>
    /// <returns>Die Kennungen derer, die neu dazugekommen sind.</returns>
    public async Task<IReadOnlyList<Guid>> AddParticipantsAsync(
        Guid conversationId,
        IReadOnlyList<Guid>? userIds,
        IReadOnlyList<Guid>? groupIds,
        CancellationToken cancellationToken = default)
    {
        Conversation conversation = await LoadVisibleAsync(conversationId, cancellationToken);

        if (conversation.Kind == ConversationKind.Direct)
        {
            throw new ConversationException(
                ["Eine Direktnachricht bleibt zu zweit. Für mehrere legt man einen Kanal an."]);
        }

        if (conversation.GroupId is not null)
        {
            throw new ConversationException(
                ["Dieser Kanal folgt seiner Gruppe. Wer dazukommen soll, kommt in die Gruppe (Einstellungen → Zugänge)."]);
        }

        List<Guid> wanted = [.. userIds ?? []];
        if (groupIds is { Count: > 0 })
        {
            wanted.AddRange(await context.GroupMembers
                .Where(m => groupIds.Contains(m.GroupId))
                .Select(m => m.UserId)
                .ToListAsync(cancellationToken));
        }

        List<Guid> known = await context.Users
            .Where(u => u.IsActive && wanted.Contains(u.Id))
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        DateTimeOffset now = timeProvider.GetUtcNow();
        List<Guid> added = [];

        foreach (Guid userId in known.Distinct())
        {
            ConversationParticipant? existing = conversation.Participants.FirstOrDefault(p => p.UserId == userId);
            if (existing is null)
            {
                Join(conversation, userId, now);
                added.Add(userId);
            }
            else if (existing.LeftAt is not null)
            {
                existing.LeftAt = null;
                existing.ReadUpTo = now;
                added.Add(userId);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        return added;
    }

    /// <summary>
    /// Jemand geht -- selbst oder herausgenommen. Die Zeile bleibt mit
    /// <see cref="ConversationParticipant.LeftAt"/> stehen, damit nachlesbar
    /// ist, wer wann mitgelesen hat.
    /// </summary>
    public async Task LeaveAsync(Guid conversationId, Guid? userId = null, CancellationToken cancellationToken = default)
    {
        Conversation conversation = await LoadVisibleAsync(conversationId, cancellationToken);
        Guid who = userId ?? Me;

        if (who != Me && conversation.Kind == ConversationKind.Direct)
        {
            throw new ConversationException(["Aus einer Direktnachricht nimmt man niemanden heraus."]);
        }

        // Herausnehmen geht ueber die Gruppe. Selbst gehen geht -- bis die
        // Gruppe sich das naechste Mal aendert; stumm schalten haelt laenger.
        if (who != Me && conversation.GroupId is not null)
        {
            throw new ConversationException(
                ["Dieser Kanal folgt seiner Gruppe. Wer gehen soll, verlässt die Gruppe."]);
        }

        ConversationParticipant? participant = conversation.Participants
            .FirstOrDefault(p => p.UserId == who && p.LeftAt == null);
        if (participant is null)
        {
            return;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        participant.LeftAt = now;

        // Ein geschlossener Kanal, in dem niemand mehr ist, waere fuer alle
        // unsichtbar und doch da. Dann lieber archiviert.
        if (conversation.IsPrivate && conversation.Participants.All(p => p.LeftAt is not null))
        {
            conversation.ArchivedAt ??= now;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Die Konten des Hauses zum Auswaehlen -- fuer Teilnehmer und Erwaehnungen.</summary>
    public async Task<IReadOnlyList<(Guid Id, string Name)>> PeopleAsync(CancellationToken cancellationToken = default)
    {
        var rows = await context.Users
            .Where(u => u.IsActive)
            .Select(u => new { u.Id, u.GivenName, u.Surname })
            .ToListAsync(cancellationToken);

        return rows
            .Select(u => (u.Id, Name: $"{u.GivenName} {u.Surname}".Trim()))
            .OrderBy(u => u.Name, StringComparer.Create(new System.Globalization.CultureInfo("de-DE"), true))
            .ToList();
    }

    private async Task<Conversation> AddAsync(Conversation conversation, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> problems = conversation.Validate();
        if (problems.Count > 0)
        {
            throw new ConversationException(problems);
        }

        context.Conversations.Add(conversation);
        await context.SaveChangesAsync(cancellationToken);
        return conversation;
    }

    /// <summary>
    /// Nimmt jemanden in eine Unterhaltung auf, die schon in der Datenbank steht.
    /// </summary>
    /// <remarks>
    /// Ueber den DbSet und nicht nur ueber die Navigation: Unsere Entitaeten
    /// vergeben ihre Id schon beim Erzeugen. Haengt man ein solches Objekt an
    /// die Sammlung einer bereits geladenen Entitaet, haelt EF Core es wegen des
    /// gesetzten Schluessels fuer vorhanden und schreibt ein UPDATE auf eine
    /// Zeile, die es nicht gibt -- mit einem Nebenlaeufigkeitsfehler als Quittung.
    /// </remarks>
    private void Join(Conversation conversation, Guid userId, DateTimeOffset at)
    {
        var participant = new ConversationParticipant
        {
            ConversationId = conversation.Id,
            UserId = userId,
            JoinedAt = at,
            ReadUpTo = at,
        };

        context.ConversationParticipants.Add(participant);
        conversation.Participants.Add(participant);
    }

    private void AddParticipants(Conversation conversation, IEnumerable<Guid> userIds)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        foreach (Guid id in userIds.Distinct())
        {
            conversation.Participants.Add(new ConversationParticipant { UserId = id, JoinedAt = now });
        }
    }

    private async Task<Conversation> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Conversations
            .Include(c => c.Participants)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
        ?? throw new ConversationException(["Diese Unterhaltung gibt es nicht."]);

    /// <summary>
    /// Wie <see cref="LoadAsync"/>, aber nur, was diese Person sehen darf. Fuer
    /// alles, was eine Unterhaltung veraendert: Wer einen geschlossenen Kanal
    /// nicht sieht, soll ihn auch nicht archivieren oder fuellen koennen.
    /// </summary>
    private async Task<Conversation> LoadVisibleAsync(Guid id, CancellationToken cancellationToken) =>
        await Visible()
            .Include(c => c.Participants)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
        ?? throw new ConversationException(["Diese Unterhaltung gibt es nicht."]);
}

public class ConversationException(IReadOnlyList<string> problems)
    : InvalidOperationException(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}
