using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Contacts;
using Souffleur.Domain.Personnel;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Personnel;

public class TimeEntryException(IReadOnlyList<string> problems)
    : Exception(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;

    public TimeEntryException(string problem) : this([problem])
    {
    }
}

/// <summary>
/// Kommen und Gehen, und was daraus wird.
/// </summary>
/// <remarks>
/// Die Tagesgrenzen zieht nicht dieser Dienst, sondern die Oberflaeche: Ein
/// Zeitpunkt ist ein Zeitpunkt, erst eine Zeitzone macht einen Tag daraus. Fuer
/// ein Haus, dessen Leute vor Ort sitzen, ist das die Zeitzone des Browsers --
/// dieselbe Ueberlegung wie im Kalender.
/// </remarks>
public class TimeClockService(SouffleurDbContext context, TimeProvider time)
{
    /// <summary>Wie viele Eintraege eine Abfrage hoechstens liefert.</summary>
    public const int MaxResults = 500;

    /// <summary>
    /// Der Kontakt zu einem Benutzerkonto. Ohne ihn kann jemand sich zwar
    /// anmelden, aber nicht stempeln -- dann fehlt die Person zum Konto.
    /// </summary>
    public Task<Contact?> ContactOfAsync(Guid userId, CancellationToken token = default) =>
        context.Contacts
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId, token);

    /// <summary>Die laufende Zeit dieser Person, falls eine laeuft.</summary>
    public Task<TimeEntry?> RunningAsync(Guid contactId, CancellationToken token = default) =>
        context.TimeEntries
            .Include(e => e.Function)
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.ContactId == contactId && e.EndedAt == null, token);

    public async Task<TimeEntry> ClockInAsync(
        Guid contactId,
        Guid? functionId = null,
        Guid? eventId = null,
        string? note = null,
        CancellationToken token = default)
    {
        await EnsureContactAsync(contactId, token);

        if (await context.TimeEntries.AnyAsync(e => e.ContactId == contactId && e.EndedAt == null, token))
        {
            throw new TimeEntryException(
                "Es läuft schon eine Zeit. Erst gehen, dann wieder kommen.");
        }

        TimeEntry entry = new()
        {
            ContactId = contactId,
            StartedAt = time.GetUtcNow(),
            Source = TimeEntrySource.Clock,
            FunctionId = functionId,
            EventId = eventId,
            Note = note,
        };

        context.TimeEntries.Add(entry);
        await context.SaveChangesAsync(token);
        return entry;
    }

    public async Task<TimeEntry> ClockOutAsync(
        Guid contactId,
        int breakMinutes = 0,
        CancellationToken token = default)
    {
        TimeEntry entry = await context.TimeEntries
            .FirstOrDefaultAsync(e => e.ContactId == contactId && e.EndedAt == null, token)
            ?? throw new TimeEntryException("Es läuft keine Zeit, die sich beenden ließe.");

        entry.EndedAt = time.GetUtcNow();
        entry.BreakMinutes = breakMinutes;

        Check(entry);
        await EnsureFreeAsync(entry, token);
        await context.SaveChangesAsync(token);
        return entry;
    }

    /// <summary>
    /// Eine laufende Zeit verwerfen -- versehentlich gestempelt.
    /// </summary>
    /// <remarks>
    /// Nur solange sie laeuft. Eine abgeschlossene Zeit wird korrigiert und
    /// nicht geloescht: Was einmal als gearbeitet dastand, soll nicht spurlos
    /// verschwinden koennen.
    /// </remarks>
    public async Task DiscardRunningAsync(Guid contactId, CancellationToken token = default)
    {
        TimeEntry entry = await context.TimeEntries
            .FirstOrDefaultAsync(e => e.ContactId == contactId && e.EndedAt == null, token)
            ?? throw new TimeEntryException("Es läuft keine Zeit, die sich verwerfen ließe.");

        context.TimeEntries.Remove(entry);
        await context.SaveChangesAsync(token);
    }

    /// <summary>Eine Zeit nachtragen, die niemand gestempelt hat.</summary>
    public async Task<TimeEntry> AddAsync(TimeEntryInput input, CancellationToken token = default)
    {
        await EnsureContactAsync(input.ContactId, token);

        TimeEntry entry = new()
        {
            ContactId = input.ContactId,
            StartedAt = input.StartedAt,
            EndedAt = input.EndedAt,
            BreakMinutes = input.BreakMinutes,
            FunctionId = input.FunctionId,
            EventId = input.EventId,
            Note = input.Note,
            Source = TimeEntrySource.Manual,
        };

        if (entry.EndedAt is null)
        {
            throw new TimeEntryException("Eine nachgetragene Zeit braucht ein Ende.");
        }

        Check(entry);
        await EnsureFreeAsync(entry, token);

        context.TimeEntries.Add(entry);
        await context.SaveChangesAsync(token);
        return entry;
    }

    /// <summary>
    /// Eine abgeschlossene Zeit richtigstellen. Ohne Grund geht es nicht.
    /// </summary>
    public async Task<TimeEntry> CorrectAsync(
        Guid id,
        TimeEntryInput input,
        string reason,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new TimeEntryException(
                "Eine Korrektur braucht einen Grund. Eine stillschweigend verschobene "
                + "Arbeitszeit ist der Grund, warum Aufzeichnungen angezweifelt werden.");
        }

        TimeEntry entry = await context.TimeEntries.FirstOrDefaultAsync(e => e.Id == id, token)
            ?? throw new TimeEntryException("Diesen Zeiteintrag gibt es nicht.");

        if (entry.IsRunning)
        {
            throw new TimeEntryException(
                "Diese Zeit läuft noch. Erst beenden, dann richtigstellen.");
        }

        entry.StartedAt = input.StartedAt;
        entry.EndedAt = input.EndedAt;
        entry.BreakMinutes = input.BreakMinutes;
        entry.FunctionId = input.FunctionId;
        entry.EventId = input.EventId;
        entry.Note = input.Note;
        entry.CorrectionReason = reason.Trim();

        Check(entry);
        await EnsureFreeAsync(entry, token);
        await context.SaveChangesAsync(token);
        return entry;
    }

    /// <summary>
    /// Die Zeiten eines Zeitraums. Ohne Kontakt: alle -- das sieht nur, wer
    /// Personalzahlen sehen darf; darueber wacht der Endpunkt.
    /// </summary>
    public async Task<List<TimeEntry>> ListAsync(
        Guid? contactId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken token = default)
    {
        IQueryable<TimeEntry> query = context.TimeEntries
            .Include(e => e.Contact)
            .Include(e => e.Function)
            .AsNoTracking()
            .Where(e => e.StartedAt < to && (e.EndedAt == null || e.EndedAt > from));

        if (contactId is { } contact)
        {
            query = query.Where(e => e.ContactId == contact);
        }

        return await query
            .OrderBy(e => e.StartedAt)
            .Take(MaxResults)
            .ToListAsync(token);
    }

    private static void Check(TimeEntry entry)
    {
        IReadOnlyList<string> problems = entry.Validate();

        if (problems.Count > 0)
        {
            throw new TimeEntryException(problems);
        }
    }

    private async Task EnsureContactAsync(Guid contactId, CancellationToken token)
    {
        if (!await context.Contacts.AnyAsync(c => c.Id == contactId && c.IsActive, token))
        {
            throw new TimeEntryException("Diesen Kontakt gibt es nicht oder er ist stillgelegt.");
        }
    }

    /// <summary>
    /// Niemand arbeitet zweimal zur selben Zeit. Eine Ueberschneidung ist fast
    /// immer ein vergessenes Gehen -- und sie wuerde den Saldo verdoppeln.
    /// </summary>
    private async Task EnsureFreeAsync(TimeEntry entry, CancellationToken token)
    {
        DateTimeOffset end = entry.EndedAt ?? DateTimeOffset.MaxValue;

        bool overlaps = await context.TimeEntries.AnyAsync(
            other => other.Id != entry.Id
                && other.ContactId == entry.ContactId
                && other.StartedAt < end
                && (other.EndedAt == null || other.EndedAt > entry.StartedAt),
            token);

        if (overlaps)
        {
            throw new TimeEntryException(
                "In dieser Spanne steht schon eine Zeit dieser Person.");
        }
    }
}

/// <summary>Was eine nachgetragene oder richtiggestellte Zeit mitbringt.</summary>
public record TimeEntryInput(
    Guid ContactId,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    int BreakMinutes,
    Guid? FunctionId,
    Guid? EventId,
    string? Note);
