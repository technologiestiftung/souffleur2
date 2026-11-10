using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Calendar;
using Souffleur.Domain.Modules;
using Souffleur.Domain.Resources;
using Souffleur.Domain.Work;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Calendar;

/// <summary>
/// Abonnements auf den Kalender: anlegen, zurückziehen und ausliefern.
/// </summary>
/// <remarks>
/// Was hinausgeht, ist kein Export, sondern ein Abonnement -- siehe ADR-0006.
/// Die Kalenderanwendung fragt die Adresse regelmäßig ab; was sich in
/// souffleur ändert, ändert sich beim nächsten Abgleich auch dort.
/// </remarks>
public class SubscriptionService(SouffleurDbContext context, TimeProvider time)
{
    /// <summary>
    /// Wie weit der Feed zurückreicht.
    /// </summary>
    /// <remarks>
    /// Einzeltermine von vor zehn Jahren interessieren niemanden mehr und
    /// machen den Feed mit jedem Jahr langsamer. Serien stehen davon
    /// unberührt: Sie gehen als Regel hinaus und kosten eine Zeile, egal wie
    /// lange es sie gibt.
    /// </remarks>
    public const int PastDays = 180;

    public async Task<List<CalendarSubscription>> ListAsync(
        Guid? ownerId, bool all = false, CancellationToken token = default) =>
        await context.CalendarSubscriptions
            .Where(s => all || s.OwnerId == ownerId)
            .OrderBy(s => s.Name)
            .AsNoTracking()
            .ToListAsync(token);

    public async Task<CalendarSubscription> AddAsync(
        CalendarSubscription subscription, CancellationToken token = default)
    {
        Validate(subscription);
        subscription.Token = NewToken();
        context.CalendarSubscriptions.Add(subscription);
        await context.SaveChangesAsync(token);
        return subscription;
    }

    public async Task<CalendarSubscription> UpdateAsync(
        Guid id, Action<CalendarSubscription> apply, CancellationToken token = default)
    {
        CalendarSubscription found = await FindAsync(id, token);
        apply(found);
        Validate(found);
        await context.SaveChangesAsync(token);
        return found;
    }

    /// <summary>
    /// Ein neues Token: Die alte Adresse antwortet ab sofort nicht mehr.
    /// </summary>
    /// <remarks>
    /// Der einzige Weg, ein weitergegebenes Abonnement wieder einzufangen --
    /// wer die Adresse hat, braucht kein Konto. Alle, die es weiter nutzen
    /// sollen, müssen die neue eintragen.
    /// </remarks>
    public async Task<CalendarSubscription> RotateAsync(
        Guid id, CancellationToken token = default)
    {
        CalendarSubscription found = await FindAsync(id, token);
        found.Token = NewToken();
        found.LastFetchedAt = null;
        found.FetchCount = 0;
        await context.SaveChangesAsync(token);
        return found;
    }

    public async Task DeleteAsync(Guid id, CancellationToken token = default)
    {
        CalendarSubscription found = await FindAsync(id, token);
        context.CalendarSubscriptions.Remove(found);
        await context.SaveChangesAsync(token);
    }

    /// <summary>
    /// Den Feed zu einer Adresse bauen. <c>null</c> heißt: Diese Adresse gibt
    /// es nicht oder nicht mehr.
    /// </summary>
    public async Task<IcsCalendar?> FeedAsync(string feedToken, CancellationToken token = default)
    {
        CalendarSubscription? subscription = await context.CalendarSubscriptions
            .FirstOrDefaultAsync(s => s.Token == feedToken && s.IsActive, token);

        if (subscription is null)
        {
            return null;
        }

        TimeZoneInfo zone = await ZoneAsync(token);
        DateTimeOffset from = time.GetUtcNow().AddDays(-PastDays);

        List<CalendarEvent> events = await context.CalendarEvents
            .Where(e => e.SeriesId == null && e.EndsAt >= from)
            .AsNoTracking()
            .ToListAsync(token);

        List<EventSeries> series = await context.EventSeries
            .Include(s => s.Exceptions)
            .AsNoTracking()
            .ToListAsync(token);

        // Fristen gehören den Aufgaben; ist das Modul aus, gibt es keine.
        List<TaskItem> deadlines = await TasksEnabledAsync(token)
            ? await context.TaskItems
                .Where(t => t.DueDate != null
                            && t.DueDate >= DateOnly.FromDateTime(from.UtcDateTime)
                            && t.State != TaskState.Done
                            && t.State != TaskState.Dropped)
                .AsNoTracking()
                .ToListAsync(token)
            : [];

        if (subscription.CategoryIds.Count > 0)
        {
            HashSet<Guid> wanted = [.. subscription.CategoryIds];
            events = [.. events.Where(e => e.CategoryId is { } id && wanted.Contains(id))];
            series = [.. series.Where(s => s.CategoryId is { } id && wanted.Contains(id))];
            deadlines = [.. deadlines.Where(t => t.CategoryId is { } id && wanted.Contains(id))];
        }

        if (subscription.ResourceIds.Count > 0)
        {
            HashSet<Guid> wanted = [.. subscription.ResourceIds];

            List<ResourceBooking> bookings = await context.ResourceBookings
                .Where(b => wanted.Contains(b.ResourceId))
                .AsNoTracking()
                .ToListAsync(token);

            HashSet<Guid> keepEvents = [.. bookings.Where(b => b.EventId is not null)
                                                   .Select(b => b.EventId!.Value)];
            HashSet<Guid> keepSeries = [.. bookings.Where(b => b.SeriesId is not null)
                                                   .Select(b => b.SeriesId!.Value)];

            events = [.. events.Where(e => keepEvents.Contains(e.Id))];
            series = [.. series.Where(s => keepSeries.Contains(s.Id))];

            // Eine Frist belegt keinen Raum. Wer nach Räumen filtert, meint
            // den Spielplan, nicht die Aufgabenliste.
            deadlines = [];
        }

        if (subscription.ContactIds.Count > 0)
        {
            HashSet<Guid> wanted = [.. subscription.ContactIds];

            List<EventParticipant> involved = await context.EventParticipants
                .Where(p => wanted.Contains(p.ContactId)
                            && p.Status != ParticipationStatus.Declined)
                .AsNoTracking()
                .ToListAsync(token);

            HashSet<Guid> keepEvents = [.. involved.Where(p => p.EventId is not null)
                                                   .Select(p => p.EventId!.Value)];
            HashSet<Guid> keepSeries = [.. involved.Where(p => p.SeriesId is not null)
                                                   .Select(p => p.SeriesId!.Value)];

            events = [.. events.Where(e => keepEvents.Contains(e.Id))];
            series = [.. series.Where(s => keepSeries.Contains(s.Id))];

            // Bei einer Frist zählt, wer zuständig oder beteiligt ist.
            deadlines = [.. deadlines.Where(t =>
                (t.AssigneeId is { } id && wanted.Contains(id))
                || t.Participants.Any(p => wanted.Contains(p.ContactId)))];
        }

        // Dass jemand abruft, wird vermerkt -- sonst ließe sich nicht
        // erkennen, welche Adressen noch in Gebrauch sind.
        subscription.LastFetchedAt = time.GetUtcNow();
        subscription.FetchCount++;
        await context.SaveChangesAsync(token);

        return new IcsCalendar(
            subscription.Name, zone, events, series, subscription.IncludeCancelled, deadlines);
    }

    private async Task<bool> TasksEnabledAsync(CancellationToken token)
    {
        ModuleSetting? setting = await context.ModuleSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Module == SouffleurModule.Workspace, token);

        return setting?.IsEnabled ?? SouffleurModule.Workspace.IsOnByDefault();
    }

    private async Task<TimeZoneInfo> ZoneAsync(CancellationToken token)
    {
        string? id = await context.Organizations
            .Select(o => o.TimeZoneId)
            .FirstOrDefaultAsync(token);

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id ?? "Europe/Berlin");
        }
        catch (Exception exception) when (
            exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private async Task<CalendarSubscription> FindAsync(Guid id, CancellationToken token) =>
        await context.CalendarSubscriptions.FirstOrDefaultAsync(s => s.Id == id, token)
        ?? throw new CalendarException("Das Abonnement gibt es nicht.");

    private static void Validate(CalendarSubscription subscription)
    {
        if (string.IsNullOrWhiteSpace(subscription.Name))
        {
            throw new CalendarException("Ein Name ist nötig.");
        }
    }

    /// <summary>
    /// Ein zufälliges Token, in der Adresse verwendbar.
    /// </summary>
    /// <remarks>
    /// 24 Bytes aus dem kryptografischen Zufall -- wer es raten will, rät an
    /// einem Passwort. Base64 mit „-" und „_" statt „+" und „/", damit nichts
    /// kodiert werden muss, und ohne „=" am Ende.
    /// </remarks>
    private static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(CalendarSubscription.TokenBytes))
               .Replace('+', '-')
               .Replace('/', '_')
               .TrimEnd('=');
}
