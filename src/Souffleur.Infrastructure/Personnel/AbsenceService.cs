using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Contacts;
using Souffleur.Domain.Organizations;
using Souffleur.Domain.Personnel;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Personnel;

public class AbsenceException(IReadOnlyList<string> problems)
    : Exception(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;

    public AbsenceException(string problem) : this([problem])
    {
    }
}

/// <summary>Was ein Antrag braucht.</summary>
public sealed record AbsenceInput(
    Guid ContactId,
    AbsenceKind Kind,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsHalfDay = false,
    Guid? DeputyId = null,
    string? Note = null,
    bool CertificateReceived = false);

/// <summary>Was eine Person im Jahr hat: Urlaub und Stunden.</summary>
public sealed record PersonBalance(
    Guid ContactId,
    string Name,
    Employment? Employment,
    LeaveBalance Leave,
    HoursBalance? Hours);

/// <summary>
/// Abwesenheiten: beantragen, entscheiden, melden -- und was daraus folgt.
/// </summary>
/// <remarks>
/// Wer was sehen und entscheiden darf, prueft der Endpunkt. Dieser Dienst
/// prueft, was unabhaengig davon nicht geht: Ein Antrag, der sich mit einem
/// anderen ueberschneidet, ein Entscheid ueber etwas, das schon entschieden
/// ist, eine Ablehnung ohne Grund.
/// </remarks>
public class AbsenceService(SouffleurDbContext context, TimeProvider time)
{
    public const int MaxResults = 1000;

    public Task<Contact?> ContactOfAsync(Guid userId, CancellationToken token = default) =>
        context.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId, token);

    /// <summary>Heute, in der Zeitzone des Hauses.</summary>
    public async Task<DateOnly> TodayAsync(CancellationToken token = default)
    {
        TimeZoneInfo zone = await ZoneAsync(token);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).DateTime);
    }

    public Task<Absence?> FindAsync(Guid id, CancellationToken token = default) =>
        context.Absences
            .Include(a => a.Contact)
            .Include(a => a.Deputy)
            .FirstOrDefaultAsync(a => a.Id == id, token);

    /// <summary>
    /// Abwesenheiten, die einen Zeitraum beruehren. Ohne Person die aller.
    /// </summary>
    public Task<List<Absence>> ListAsync(
        Guid? contactId,
        DateOnly from,
        DateOnly to,
        CancellationToken token = default) =>
        context.Absences
            .Include(a => a.Contact)
            .Include(a => a.Deputy)
            .Where(a => contactId == null || a.ContactId == contactId)
            .Where(a => a.StartDate <= to && a.EndDate >= from)
            .OrderBy(a => a.StartDate)
            .Take(MaxResults)
            .AsNoTracking()
            .ToListAsync(token);

    /// <summary>Was auf eine Entscheidung wartet, das Aelteste zuerst.</summary>
    public Task<List<Absence>> OpenRequestsAsync(CancellationToken token = default) =>
        context.Absences
            .Include(a => a.Contact)
            .Include(a => a.Deputy)
            .Where(a => a.Status == AbsenceStatus.Requested)
            .OrderBy(a => a.StartDate)
            .AsNoTracking()
            .ToListAsync(token);

    /// <summary>
    /// Krankmeldungen, Elternzeit und unbezahlter Urlaub, von denen die
    /// Lohnbuchhaltung noch nichts weiss.
    /// </summary>
    public async Task<List<Absence>> UnreportedToPayrollAsync(CancellationToken token = default)
    {
        AbsenceKind[] kinds = [.. AbsenceKinds.All.Where(k => k.ConcernsPayroll())];

        return await context.Absences
            .Include(a => a.Contact)
            .Where(a => kinds.Contains(a.Kind)
                        && (a.Status == AbsenceStatus.Approved || a.Status == AbsenceStatus.Reported)
                        && a.PayrollReportedAt == null)
            .OrderBy(a => a.StartDate)
            .AsNoTracking()
            .ToListAsync(token);
    }

    /// <summary>
    /// Einen Antrag stellen oder eine Krankheit melden. Krankheit gilt sofort;
    /// alles andere wartet auf eine Entscheidung.
    /// </summary>
    public async Task<Absence> RequestAsync(AbsenceInput input, CancellationToken token = default)
    {
        await EnsureContactAsync(input.ContactId, token);

        if (input.DeputyId is { } deputy)
        {
            await EnsureContactAsync(deputy, token);
        }

        bool sickness = input.Kind.IsSickness();

        var absence = new Absence
        {
            ContactId = input.ContactId,
            Kind = input.Kind,
            Status = sickness ? AbsenceStatus.Reported : AbsenceStatus.Requested,
            StartDate = input.StartDate,
            EndDate = input.EndDate,
            IsHalfDay = input.IsHalfDay,
            DeputyId = input.DeputyId,

            // Bei Krankheit keine Notiz, auch wenn eine kommt: Was nicht
            // gespeichert wird, kann nicht in falsche Haende geraten.
            Note = sickness ? null : Trimmed(input.Note),
            CertificateReceived = sickness && input.CertificateReceived,
        };

        Throw(absence.Validate());
        await EnsureNoOverlapAsync(absence, token);

        context.Absences.Add(absence);
        await context.SaveChangesAsync(token);

        return (await FindAsync(absence.Id, token))!;
    }

    /// <summary>Einen offenen Antrag genehmigen oder ablehnen.</summary>
    public async Task<Absence> DecideAsync(
        Guid id,
        bool approve,
        string? note,
        Guid? deciderUserId,
        CancellationToken token = default)
    {
        Absence absence = await LoadAsync(id, token);

        if (absence.Status != AbsenceStatus.Requested)
        {
            throw new AbsenceException(
                $"Der Antrag ist schon {absence.Status.Label().ToLowerInvariant()}.");
        }

        if (!approve && string.IsNullOrWhiteSpace(note))
        {
            // Eine Ablehnung ohne Grund ist eine, ueber die man streitet.
            throw new AbsenceException("Eine Ablehnung braucht einen Grund.");
        }

        if (approve)
        {
            // Zwischen Antrag und Entscheidung kann ein anderer Eintrag
            // dazugekommen sein -- eine Krankmeldung etwa.
            await EnsureNoOverlapAsync(absence, token);
        }

        absence.Status = approve ? AbsenceStatus.Approved : AbsenceStatus.Rejected;
        absence.DecidedBy = deciderUserId;
        absence.DecidedAt = time.GetUtcNow();
        absence.DecisionNote = Trimmed(note);

        await context.SaveChangesAsync(token);
        return absence;
    }

    /// <summary>
    /// Zurueckziehen oder aufheben. Ein offener Antrag laesst sich immer
    /// zurueckziehen; was schon gilt, nur mit Grund -- der Kalender und das
    /// Urlaubskonto aendern sich dadurch, und jemand soll nachlesen koennen,
    /// warum.
    /// </summary>
    public async Task<(Absence Absence, AbsenceStatus Before)> WithdrawAsync(
        Guid id,
        string? reason,
        Guid? userId,
        CancellationToken token = default)
    {
        Absence absence = await LoadAsync(id, token);
        AbsenceStatus before = absence.Status;

        if (!before.IsActive())
        {
            throw new AbsenceException(
                $"Der Eintrag ist schon {before.Label().ToLowerInvariant()}.");
        }

        if (before != AbsenceStatus.Requested && string.IsNullOrWhiteSpace(reason))
        {
            throw new AbsenceException("Was schon gilt, lässt sich nur mit Grund aufheben.");
        }

        absence.Status = AbsenceStatus.Withdrawn;
        absence.DecidedBy = userId;
        absence.DecidedAt = time.GetUtcNow();
        absence.DecisionNote = Trimmed(reason) ?? absence.DecisionNote;

        await context.SaveChangesAsync(token);
        return (absence, before);
    }

    /// <summary>
    /// Eine Krankmeldung nachfuehren: laenger krank (Folgebescheinigung) oder
    /// die Bescheinigung ist da.
    /// </summary>
    public async Task<Absence> UpdateSicknessAsync(
        Guid id,
        DateOnly endDate,
        bool certificateReceived,
        CancellationToken token = default)
    {
        Absence absence = await LoadAsync(id, token);

        if (!absence.Kind.IsSickness() || absence.Status != AbsenceStatus.Reported)
        {
            throw new AbsenceException("Nachführen lässt sich nur eine gemeldete Krankheit.");
        }

        absence.EndDate = endDate;
        absence.IsHalfDay = absence.IsHalfDay && endDate == absence.StartDate;
        absence.CertificateReceived = certificateReceived;

        Throw(absence.Validate());
        await EnsureNoOverlapAsync(absence, token);

        await context.SaveChangesAsync(token);
        return absence;
    }

    /// <summary>Vermerken, dass die Lohnbuchhaltung Bescheid weiss -- oder es zuruecknehmen.</summary>
    public async Task<Absence> MarkPayrollAsync(Guid id, bool reported, CancellationToken token = default)
    {
        Absence absence = await LoadAsync(id, token);

        if (!absence.Kind.ConcernsPayroll())
        {
            throw new AbsenceException(
                $"{absence.Kind.Label()} geht die Lohnbuchhaltung nichts an.");
        }

        absence.PayrollReportedAt = reported ? time.GetUtcNow() : null;
        await context.SaveChangesAsync(token);
        return absence;
    }

    // --- Vertraege ---

    public Task<List<Employment>> EmploymentsAsync(Guid contactId, CancellationToken token = default) =>
        context.Employments
            .Where(e => e.ContactId == contactId)
            .OrderBy(e => e.ValidFrom)
            .AsNoTracking()
            .ToListAsync(token);

    public async Task<Employment> SaveEmploymentAsync(
        Guid? id,
        Employment values,
        CancellationToken token = default)
    {
        await EnsureContactAsync(values.ContactId, token);
        Throw(values.Validate());

        Employment employment;

        if (id is { } existing)
        {
            employment = await context.Employments.FirstOrDefaultAsync(e => e.Id == existing, token)
                         ?? throw new AbsenceException("Den Vertragsstand gibt es nicht mehr.");

            employment.ValidFrom = values.ValidFrom;
            employment.EndsOn = values.EndsOn;
            employment.WeeklyHours = values.WeeklyHours;
            employment.WorkDays = values.WorkDays;
            employment.AnnualLeaveDays = values.AnnualLeaveDays;
            employment.Note = Trimmed(values.Note);
        }
        else
        {
            employment = values;
            employment.Note = Trimmed(values.Note);
            context.Employments.Add(employment);
        }

        // Zwei Staende ab demselben Tag liessen offen, welcher gilt.
        if (await context.Employments.AnyAsync(
                e => e.ContactId == employment.ContactId
                     && e.ValidFrom == employment.ValidFrom
                     && e.Id != employment.Id,
                token))
        {
            throw new AbsenceException(
                $"Ab {employment.ValidFrom:dd.MM.yyyy} gibt es schon einen Vertragsstand.");
        }

        await context.SaveChangesAsync(token);
        return employment;
    }

    public async Task RemoveEmploymentAsync(Guid id, CancellationToken token = default)
    {
        Employment? employment = await context.Employments.FirstOrDefaultAsync(e => e.Id == id, token);
        if (employment is not null)
        {
            context.Employments.Remove(employment);
            await context.SaveChangesAsync(token);
        }
    }

    public Task<List<LeaveAdjustment>> AdjustmentsAsync(
        Guid contactId, int year, CancellationToken token = default) =>
        context.LeaveAdjustments
            .Where(a => a.ContactId == contactId && a.Year == year)
            .OrderBy(a => a.CreatedAt)
            .AsNoTracking()
            .ToListAsync(token);

    public async Task<LeaveAdjustment> AddAdjustmentAsync(
        Guid contactId, int year, decimal days, string reason, CancellationToken token = default)
    {
        await EnsureContactAsync(contactId, token);

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new AbsenceException("Eine Gutschrift oder ein Abzug braucht einen Grund.");
        }

        if (days == 0 || Math.Abs(days) > 60 || days * 2 != Math.Round(days * 2))
        {
            throw new AbsenceException("Gutgeschrieben oder abgezogen werden halbe oder ganze Tage, höchstens 60.");
        }

        var adjustment = new LeaveAdjustment
        {
            ContactId = contactId,
            Year = year,
            Days = days,
            Reason = reason.Trim(),
        };

        context.LeaveAdjustments.Add(adjustment);
        await context.SaveChangesAsync(token);
        return adjustment;
    }

    public async Task RemoveAdjustmentAsync(Guid id, CancellationToken token = default)
    {
        LeaveAdjustment? adjustment = await context.LeaveAdjustments.FirstOrDefaultAsync(a => a.Id == id, token);
        if (adjustment is not null)
        {
            context.LeaveAdjustments.Remove(adjustment);
            await context.SaveChangesAsync(token);
        }
    }

    // --- Freie Tage ---

    public Task<List<HouseHoliday>> HouseHolidaysAsync(CancellationToken token = default) =>
        context.HouseHolidays
            .OrderBy(h => h.Month).ThenBy(h => h.Day)
            .AsNoTracking()
            .ToListAsync(token);

    public async Task<HouseHoliday> AddHouseHolidayAsync(
        int month, int day, string name, CancellationToken token = default)
    {
        var holiday = new HouseHoliday { Month = month, Day = day, Name = name.Trim() };
        Throw(holiday.Validate());

        if (await context.HouseHolidays.AnyAsync(h => h.Month == month && h.Day == day, token))
        {
            throw new AbsenceException("Dieser Tag ist schon frei.");
        }

        context.HouseHolidays.Add(holiday);
        await context.SaveChangesAsync(token);
        return holiday;
    }

    public async Task RemoveHouseHolidayAsync(Guid id, CancellationToken token = default)
    {
        HouseHoliday? holiday = await context.HouseHolidays.FirstOrDefaultAsync(h => h.Id == id, token);
        if (holiday is not null)
        {
            context.HouseHolidays.Remove(holiday);
            await context.SaveChangesAsync(token);
        }
    }

    /// <summary>Die gesetzlichen Feiertage des Landes und die freien Tage des Hauses.</summary>
    public async Task<List<Holiday>> HolidaysAsync(int year, CancellationToken token = default)
    {
        FederalState state = await context.Organizations
            .Select(o => (FederalState?)o.FederalState)
            .FirstOrDefaultAsync(token) ?? FederalState.BE;

        List<Holiday> days = PublicHolidays.Of(year, state);
        HashSet<DateOnly> taken = [.. days.Select(d => d.Date)];

        foreach (HouseHoliday own in await HouseHolidaysAsync(token))
        {
            if (own.Month == 2 && own.Day == 29 && !DateTime.IsLeapYear(year))
            {
                continue;
            }

            var date = new DateOnly(year, own.Month, own.Day);
            if (taken.Add(date))
            {
                days.Add(new Holiday(date, own.Name, IsHouseDay: true));
            }
        }

        return [.. days.OrderBy(d => d.Date)];
    }

    // --- Konten ---

    /// <summary>
    /// Urlaubs- und Stundenkonto einer Person fuer ein Jahr. Das Stundenkonto
    /// laeuft bis heute -- die Zukunft hat noch niemand gearbeitet.
    /// </summary>
    public async Task<PersonBalance> BalanceAsync(Guid contactId, int year, CancellationToken token = default)
    {
        Contact contact = await context.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contactId, token)
                          ?? throw new AbsenceException("Den Kontakt gibt es nicht.");

        Dictionary<Guid, PersonBalance> all = await BalancesAsync([contactId], year, token);
        return all.GetValueOrDefault(contactId)
               ?? new PersonBalance(contact.Id, contact.Name, null, new LeaveBalance(year, 0, 0, 0, 0), null);
    }

    /// <summary>Die Konten aller, die einen Vertrag im System haben.</summary>
    public async Task<List<PersonBalance>> TeamAsync(int year, CancellationToken token = default)
    {
        List<Guid> ids = await context.Employments
            .Select(e => e.ContactId)
            .Distinct()
            .ToListAsync(token);

        Dictionary<Guid, PersonBalance> all = await BalancesAsync(ids, year, token);
        return [.. all.Values.OrderBy(b => b.Name)];
    }

    private async Task<Dictionary<Guid, PersonBalance>> BalancesAsync(
        IReadOnlyCollection<Guid> contactIds,
        int year,
        CancellationToken token)
    {
        var first = new DateOnly(year, 1, 1);
        var last = new DateOnly(year, 12, 31);
        DateOnly today = await TodayAsync(token);
        TimeZoneInfo zone = await ZoneAsync(token);

        // Feiertage der Nachbarjahre mit, weil ein Urlaub ueber Silvester
        // auch im anderen Jahr Tage kostet.
        HashSet<DateOnly> holidays =
        [
            .. (await HolidaysAsync(year - 1, token)).Select(h => h.Date),
            .. (await HolidaysAsync(year, token)).Select(h => h.Date),
            .. (await HolidaysAsync(year + 1, token)).Select(h => h.Date),
        ];

        Dictionary<Guid, Contact> contacts = await context.Contacts
            .Where(c => contactIds.Contains(c.Id))
            .AsNoTracking()
            .ToDictionaryAsync(c => c.Id, token);

        ILookup<Guid, Employment> employments = (await context.Employments
                .Where(e => contactIds.Contains(e.ContactId))
                .AsNoTracking()
                .ToListAsync(token))
            .ToLookup(e => e.ContactId);

        ILookup<Guid, Absence> absences = (await context.Absences
                .Where(a => contactIds.Contains(a.ContactId)
                            && a.StartDate <= last && a.EndDate >= first)
                .AsNoTracking()
                .ToListAsync(token))
            .ToLookup(a => a.ContactId);

        // Im Speicher summiert: SQLite legt decimal als Text ab und kann es
        // nicht addieren.
        Dictionary<Guid, decimal> adjustments = (await context.LeaveAdjustments
                .Where(a => contactIds.Contains(a.ContactId) && a.Year == year)
                .AsNoTracking()
                .ToListAsync(token))
            .GroupBy(a => a.ContactId)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Days));

        // Gestempelte Zeiten nach Tag in Ortszeit. Laufende zaehlen nicht --
        // eine Zeit ohne Ende ist noch keine Dauer.
        DateTimeOffset windowStart = new(first.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        DateTimeOffset windowEnd = new(last.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        ILookup<Guid, TimeEntry> entries = (await context.TimeEntries
                .Where(e => contactIds.Contains(e.ContactId)
                            && e.EndedAt != null
                            && e.StartedAt >= windowStart.AddDays(-1)
                            && e.StartedAt < windowEnd)
                .AsNoTracking()
                .ToListAsync(token))
            .ToLookup(e => e.ContactId);

        Dictionary<Guid, PersonBalance> result = [];

        foreach (Guid id in contactIds)
        {
            if (!contacts.TryGetValue(id, out Contact? contact))
            {
                continue;
            }

            List<Employment> own = [.. employments[id]];
            var calendar = new WorkCalendar(own, holidays, absences[id]);

            Dictionary<DateOnly, int> worked = entries[id]
                .GroupBy(e => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(e.StartedAt, zone).DateTime))
                .ToDictionary(g => g.Key, g => g.Sum(e => e.WorkedMinutes ?? 0));

            // Bis gestern: Der heutige Tag ist noch nicht vorbei, und wer
            // mittags nachsieht, soll nicht mit einem halben Soll im Minus
            // stehen.
            DateOnly until = today.AddDays(-1) < last ? today.AddDays(-1) : last;
            HoursBalance? hours = own.Count > 0 && until >= first
                ? calendar.HoursOf(first, until, worked)
                : null;

            result[id] = new PersonBalance(
                id,
                contact.Name,
                calendar.EmploymentOn(today < first ? first : today > last ? last : today),
                calendar.LeaveOf(year, adjustments.GetValueOrDefault(id)),
                hours);
        }

        return result;
    }

    /// <summary>
    /// Wie viele Arbeitstage jede der Abwesenheiten die Person kostet -- in
    /// einem Zug, damit eine Liste nicht je Zeile Feiertage und Vertraege laedt.
    /// </summary>
    public async Task<Dictionary<Guid, decimal>> DaysOfAsync(
        IReadOnlyCollection<Absence> absences,
        CancellationToken token = default)
    {
        if (absences.Count == 0)
        {
            return [];
        }

        DateOnly from = absences.Min(a => a.StartDate);
        DateOnly to = absences.Max(a => a.EndDate);
        Guid[] people = [.. absences.Select(a => a.ContactId).Distinct()];

        HashSet<DateOnly> holidays = [];
        for (int year = from.Year; year <= to.Year; year++)
        {
            holidays.UnionWith((await HolidaysAsync(year, token)).Select(h => h.Date));
        }

        ILookup<Guid, Employment> employments = (await context.Employments
                .Where(e => people.Contains(e.ContactId))
                .AsNoTracking()
                .ToListAsync(token))
            .ToLookup(e => e.ContactId);

        // Auch die Nachbarn: Eine Krankmeldung im Urlaub aendert, was der
        // Urlaub kostet.
        ILookup<Guid, Absence> around = (await context.Absences
                .Where(a => people.Contains(a.ContactId) && a.StartDate <= to && a.EndDate >= from)
                .AsNoTracking()
                .ToListAsync(token))
            .ToLookup(a => a.ContactId);

        Dictionary<Guid, decimal> result = [];

        foreach (IGrouping<Guid, Absence> person in absences.GroupBy(a => a.ContactId))
        {
            List<Employment> own = [.. employments[person.Key]];

            // Ohne Vertrag im System zaehlt Montag bis Freitag -- besser eine
            // ungefaehre Zahl als keine.
            if (own.Count == 0)
            {
                own = [new Employment { ContactId = person.Key, ValidFrom = DateOnly.MinValue }];
            }

            var calendar = new WorkCalendar(own, holidays, around[person.Key]);
            foreach (Absence absence in person)
            {
                result[absence.Id] = calendar.DaysOf(absence);
            }
        }

        return result;
    }

    private async Task EnsureNoOverlapAsync(Absence absence, CancellationToken token)
    {
        AbsenceStatus[] active = [AbsenceStatus.Requested, AbsenceStatus.Approved, AbsenceStatus.Reported];

        List<Absence> overlapping = await context.Absences
            .Where(a => a.ContactId == absence.ContactId
                        && a.Id != absence.Id
                        && active.Contains(a.Status)
                        && a.StartDate <= absence.EndDate
                        && a.EndDate >= absence.StartDate)
            .AsNoTracking()
            .ToListAsync(token);

        // Krank im Urlaub ist kein Widerspruch, sondern ein Fall des
        // Bundesurlaubsgesetzes: Die Krankheit liegt ueber dem Urlaub, und die
        // Tage mit Bescheinigung gehen an den Urlaub zurueck.
        Absence? clash = overlapping.FirstOrDefault(other =>
            other.Kind.IsSickness() == absence.Kind.IsSickness());

        if (clash is not null)
        {
            throw new AbsenceException(
                $"In der Zeit steht schon {clash.Kind.Label()} "
                + $"({clash.StartDate:dd.MM.}–{clash.EndDate:dd.MM.yyyy}, {clash.Status.Label().ToLowerInvariant()}).");
        }
    }

    private async Task<Absence> LoadAsync(Guid id, CancellationToken token) =>
        await context.Absences
            .Include(a => a.Contact)
            .Include(a => a.Deputy)
            .FirstOrDefaultAsync(a => a.Id == id, token)
        ?? throw new AbsenceException("Den Eintrag gibt es nicht mehr.");

    private async Task EnsureContactAsync(Guid contactId, CancellationToken token)
    {
        if (!await context.Contacts.AnyAsync(c => c.Id == contactId, token))
        {
            throw new AbsenceException("Den Kontakt gibt es nicht.");
        }
    }

    private async Task<TimeZoneInfo> ZoneAsync(CancellationToken token)
    {
        string? id = await context.Organizations.Select(o => o.TimeZoneId).FirstOrDefaultAsync(token);

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id ?? "Europe/Berlin");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        }
    }

    private static void Throw(IReadOnlyList<string> problems)
    {
        if (problems.Count > 0)
        {
            throw new AbsenceException(problems);
        }
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
