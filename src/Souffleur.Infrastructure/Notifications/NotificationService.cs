using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Souffleur.Domain.Notifications;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Notifications;

/// <summary>
/// Stellt eine neue Benachrichtigung sofort zu -- in der Glocke, ohne dass
/// die Oberflaeche fragen muss. Die Umsetzung sitzt im API-Projekt, weil nur
/// das SignalR kennt; hier reicht die Absicht.
/// </summary>
public interface INotificationPusher
{
    Task PushAsync(Guid userId, Notification notification, CancellationToken cancellationToken = default);
}

/// <summary>Fuer Tests und Hintergrunddienste: stellt nichts zu.</summary>
public sealed class NoPusher : INotificationPusher
{
    public Task PushAsync(Guid userId, Notification notification, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

/// <summary>Was benachrichtigt werden soll -- der Text steht fest, bevor er verteilt wird.</summary>
public record NotificationDraft(NotificationKind Kind, string Title, string? Body = null, string? Link = null);

/// <summary>
/// Die Glocke. Legt Benachrichtigungen an, liest und quittiert sie.
/// </summary>
public class NotificationService(
    SouffleurDbContext context,
    ICurrentUser currentUser,
    INotificationPusher pusher,
    TimeProvider time,
    ILogger<NotificationService> logger)
{
    private const int Anzeige = 50;

    /// <summary>
    /// Benachrichtigt die genannten Personen -- ausser der, die gerade
    /// handelt. Wer sich selbst eine Aufgabe gibt, braucht keinen Hinweis
    /// darauf.
    /// </summary>
    /// <remarks>
    /// Scheitert hier etwas, scheitert nicht die Handlung, die es ausgeloest
    /// hat: Eine Aufgabe ist angelegt, auch wenn die Glocke nicht klingelt.
    /// Darum faengt der Aufrufer nichts ab, sondern diese Methode.
    /// </remarks>
    public async Task NotifyAsync(
        IEnumerable<Guid> userIds,
        NotificationDraft draft,
        CancellationToken cancellationToken = default)
    {
        List<Guid> empfaenger = [.. userIds.Distinct().Where(id => id != currentUser.Id)];
        if (empfaenger.Count == 0)
        {
            return;
        }

        try
        {
            // Nur aktive Konten -- ein stillgelegtes liest keine Glocke.
            List<Guid> aktiv = await context.Users
                .Where(u => empfaenger.Contains(u.Id) && u.IsActive)
                .Select(u => u.Id)
                .ToListAsync(cancellationToken);

            List<Notification> neu =
            [
                .. aktiv.Select(id => new Notification
                {
                    UserId = id,
                    Kind = draft.Kind,
                    Title = Kuerzen(draft.Title, 300)!,
                    Body = Kuerzen(draft.Body, 1000),
                    Link = Kuerzen(draft.Link, 300),
                }),
            ];

            context.Notifications.AddRange(neu);
            await context.SaveChangesAsync(cancellationToken);

            foreach (Notification n in neu)
            {
                await pusher.PushAsync(n.UserId, n, cancellationToken);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Die Handlung ist durch -- siehe oben. Aber nicht still: Eine
            // Glocke, die nie klingelt, faellt sonst niemandem auf.
            logger.LogWarning(exception, "Benachrichtigung „{Title}“ ging nicht durch.", draft.Title);

            // Was nicht gespeichert wurde, darf nicht im Kontext haengen
            // bleiben -- das naechste Speichern des Aufrufers risse es mit.
            foreach (var eintrag in context.ChangeTracker.Entries<Notification>()
                         .Where(e => e.State == EntityState.Added)
                         .ToList())
            {
                eintrag.State = EntityState.Detached;
            }
        }
    }

    /// <summary>Wie die handelnde Person heisst -- fuer „Anna Krause hat dir …".</summary>
    public async Task<string> ActorNameAsync(CancellationToken cancellationToken = default)
    {
        if (currentUser.Id is not { } userId)
        {
            return "souffleur";
        }

        var user = await context.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.GivenName, u.Surname })
            .FirstOrDefaultAsync(cancellationToken);
        return user is null ? "Jemand" : $"{user.GivenName} {user.Surname}".Trim();
    }

    /// <summary>Die Konten hinter den Kontakten -- Aufgaben und Dienste haengen an Kontakten.</summary>
    public async Task<List<Guid>> UsersOfContactsAsync(
        IEnumerable<Guid> contactIds, CancellationToken cancellationToken = default)
    {
        List<Guid> ids = [.. contactIds.Distinct()];
        return await context.Contacts
            .Where(c => ids.Contains(c.Id) && c.UserId != null)
            .Select(c => c.UserId!.Value)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Wer in den Gruppen ist, aktiv und ohne Doppelte.</summary>
    public async Task<List<Guid>> UsersOfGroupsAsync(
        IEnumerable<Guid> groupIds, CancellationToken cancellationToken = default)
    {
        List<Guid> ids = [.. groupIds.Distinct()];
        return await context.GroupMembers
            .Where(m => ids.Contains(m.GroupId))
            .Join(context.Groups.Where(g => g.IsActive), m => m.GroupId, g => g.Id, (m, _) => m.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<(List<Notification> Items, int Unread)> MineAsync(CancellationToken cancellationToken = default)
    {
        if (currentUser.Id is not { } userId)
        {
            return ([], 0);
        }

        List<Notification> items = await context.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(Anzeige)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        int unread = await context.Notifications
            .CountAsync(n => n.UserId == userId && n.ReadAt == null, cancellationToken);

        return (items, unread);
    }

    public async Task MarkReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (currentUser.Id is not { } userId)
        {
            return;
        }

        DateTimeOffset jetzt = time.GetUtcNow();
        await context.Notifications
            .Where(n => n.Id == id && n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, jetzt), cancellationToken);
    }

    public async Task MarkAllReadAsync(CancellationToken cancellationToken = default)
    {
        if (currentUser.Id is not { } userId)
        {
            return;
        }

        DateTimeOffset jetzt = time.GetUtcNow();
        await context.Notifications
            .Where(n => n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, jetzt), cancellationToken);
    }

    // ── Einstellungen ────────────────────────────────────────────────────

    /// <summary>Die E-Mail-Wahl der angemeldeten Person je Art. Fehlt eine, gilt „aus".</summary>
    public async Task<Dictionary<NotificationKind, EmailMode>> SettingsAsync(CancellationToken cancellationToken = default)
    {
        Guid userId = currentUser.Id ?? Guid.Empty;
        return await SettingsOfAsync(userId, cancellationToken);
    }

    public async Task<Dictionary<NotificationKind, EmailMode>> SettingsOfAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        List<string> schluessel = [.. NotificationKinds.All.Select(k => k.PreferenceKey())];
        Dictionary<string, string> gespeichert = await context.UserPreferences
            .Where(p => p.UserId == userId && schluessel.Contains(p.Key))
            .ToDictionaryAsync(p => p.Key, p => p.Value, cancellationToken);

        return NotificationKinds.All.ToDictionary(
            k => k,
            k => gespeichert.TryGetValue(k.PreferenceKey(), out string? wert) && Enum.TryParse(wert, out EmailMode mode)
                ? mode
                : EmailMode.Off);
    }

    public async Task SaveSettingsAsync(
        IReadOnlyDictionary<NotificationKind, EmailMode> settings,
        CancellationToken cancellationToken = default)
    {
        if (currentUser.Id is not { } userId)
        {
            return;
        }

        foreach ((NotificationKind kind, EmailMode mode) in settings)
        {
            string key = kind.PreferenceKey();
            var vorhanden = await context.UserPreferences
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Key == key, cancellationToken);

            if (vorhanden is null)
            {
                context.UserPreferences.Add(new Domain.Identity.UserPreference
                {
                    UserId = userId,
                    Key = key,
                    Value = mode.ToString(),
                });
            }
            else
            {
                vorhanden.Value = mode.ToString();
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static string? Kuerzen(string? text, int laenge) =>
        text is null ? null : text.Length <= laenge ? text : text[..(laenge - 1)] + "…";
}
