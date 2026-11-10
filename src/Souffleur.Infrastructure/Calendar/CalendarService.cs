using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Calendar;
using Souffleur.Domain.Catalogs;
using Souffleur.Domain.Contacts;
using Souffleur.Domain.Modules;
using Souffleur.Domain.Organizations;
using Souffleur.Domain.Personnel;
using Souffleur.Domain.Resources;
using Souffleur.Domain.Touring;
using Souffleur.Domain.Work;
using Souffleur.Infrastructure.Persistence;
using Souffleur.Infrastructure.Resources;

namespace Souffleur.Infrastructure.Calendar;

/// <summary>
/// Termine, Serien und die Auflösung der Regeln -- siehe ADR-0006.
/// </summary>
public class CalendarService(SouffleurDbContext context)
{
    /// <summary>
    /// Wie weit eine Abfrage reichen darf. Nicht Bequemlichkeit, sondern
    /// Schutz: Eine tägliche Serie über hundert Jahre legte die Anwendung lahm.
    /// Zwei Jahre deckt auch die längste Spielzeitplanung ab.
    /// </summary>
    public const int MaxRangeDays = 800;

    private TimeZoneInfo? zone;

    /// <summary>
    /// Die Zeitzone des Hauses. Alle Zeitpunkte liegen in UTC; diese Angabe
    /// entscheidet, welcher Tag das im Raster ist.
    /// </summary>
    public async Task<TimeZoneInfo> ZoneAsync(CancellationToken token = default)
    {
        if (zone is not null)
        {
            return zone;
        }

        string? id = await context.Organizations
            .Select(o => o.TimeZoneId)
            .FirstOrDefaultAsync(token);

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id ?? "Europe/Berlin");
        }
        catch (Exception exception) when (
            exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.Utc;
        }

        return zone;
    }

    /// <summary>
    /// Alle Einträge eines Zeitraums -- einzelne Termine und die ausgerechneten
    /// Vorkommen der Serien, mit den Abweichungen darübergelegt.
    /// </summary>
    public async Task<List<CalendarOccurrence>> RangeAsync(
        DateOnly from,
        DateOnly to,
        bool includeCancelled = true,
        IReadOnlyCollection<Guid>? categories = null,
        IReadOnlyCollection<Guid>? resources = null,
        IReadOnlyCollection<Guid>? people = null,
        CancellationToken token = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        if (to.DayNumber - from.DayNumber > MaxRangeDays)
        {
            throw new CalendarException(
                $"Der Zeitraum ist länger als {MaxRangeDays} Tage. " +
                "Bitte in kleineren Abschnitten abfragen.");
        }

        TimeZoneInfo timeZone = await ZoneAsync(token);

        // Das Fenster in UTC: vom Beginn des ersten Tages bis zum Ende des
        // letzten -- beide in Ortszeit gemeint, sonst fehlt je nach Jahreszeit
        // eine Stunde am Rand.
        DateTimeOffset windowStart = EventSeries.ToInstant(from, TimeOnly.MinValue, timeZone);
        DateTimeOffset windowEnd = EventSeries.ToInstant(to.AddDays(1), TimeOnly.MinValue, timeZone);

        Dictionary<Guid, CatalogEntry> categoryById = await context.CatalogEntries
            .Where(e => e.Catalog == CatalogKind.EventCategory)
            .AsNoTracking()
            .ToDictionaryAsync(e => e.Id, token);

        // Alles, was im Fenster liegt: Einzeltermine und verschobene
        // Serienvorkommen gleichermaßen -- eine Abweichung kann in den Zeitraum
        // hinein verschoben worden sein, obwohl ihr Serientermin außerhalb lag.
        List<CalendarEvent> events = await context.CalendarEvents
            .Where(e => e.StartsAt < windowEnd && e.EndsAt > windowStart)
            .AsNoTracking()
            .ToListAsync(token);

        List<EventSeries> series = await context.EventSeries
            .Where(s => s.StartDate <= to && (s.Until == null || s.Until >= from))
            .AsNoTracking()
            .ToListAsync(token);

        // Die Abweichungen aller beteiligten Serien, auch die außerhalb des
        // Fensters: Sie entscheiden, welche errechneten Vorkommen entfallen.
        List<Guid> seriesIds = [.. series.Select(s => s.Id)];
        var exceptions = await context.CalendarEvents
            .Where(e => e.SeriesId != null
                        && seriesIds.Contains(e.SeriesId!.Value)
                        && e.OccurrenceStart != null)
            .Select(e => new { Series = e.SeriesId!.Value, Occurrence = e.OccurrenceStart!.Value })
            .AsNoTracking()
            .ToListAsync(token);

        // Nach UTC vereinheitlicht: Der errechnete Zeitpunkt trägt den Abstand
        // der Ortszeit, der gespeicherte kommt als UTC zurück. Verglichen wird
        // der Zeitpunkt, nicht seine Schreibweise.
        HashSet<(Guid Series, DateTime Occurrence)> overridden =
            [.. exceptions.Select(x => (x.Series, x.Occurrence.UtcDateTime))];

        Attachments attached = await AttachmentsOfAsync(
            [.. events.Select(e => e.Id)], seriesIds, token);

        List<CalendarOccurrence> result = [];

        foreach (CalendarEvent stored in events)
        {
            result.Add(ToOccurrence(
                stored,
                timeZone,
                categoryById,
                Taken(attached.ResourcesByEvent, stored.Id),
                Taken(attached.PeopleByEvent, stored.Id)));
        }

        foreach (EventSeries rule in series)
        {
            var duration = TimeSpan.FromMinutes(Math.Max(1, rule.DurationMinutes));
            TimeZoneInfo ruleZone = rule.Zone();

            foreach (DateOnly date in rule.LocalDates(from, to))
            {
                DateTimeOffset starts = EventSeries.ToInstant(date, rule.StartTime, ruleZone);

                if (overridden.Contains((rule.Id, starts.UtcDateTime)))
                {
                    // Für dieses Vorkommen gibt es einen eigenen Datensatz --
                    // verschoben, geändert oder abgesagt. Der steht schon oben.
                    continue;
                }

                DateTimeOffset ends = starts + duration;
                result.Add(new CalendarOccurrence(
                    EventId: null,
                    SourceId: rule.Id,
                    SeriesId: rule.Id,
                    OccurrenceStart: starts,
                    Title: rule.Title,
                    Description: rule.Description,
                    Location: rule.Location,
                    CategoryId: rule.CategoryId,
                    CategoryLabel: Label(rule.CategoryId, categoryById),
                    CategoryColor: Color(rule.CategoryId, categoryById),
                    Status: rule.Status,
                    StartsAt: starts,
                    EndsAt: ends,
                    LocalDate: date,
                    LocalEndDate: DateOnly.FromDateTime(
                        TimeZoneInfo.ConvertTime(ends, timeZone).DateTime),
                    IsAllDay: rule.IsAllDay,
                    IsFromSeries: true,
                    Source: CalendarSource.Calendar,
                    Resources: Taken(attached.ResourcesBySeries, rule.Id),
                    Participants: Taken(attached.PeopleBySeries, rule.Id)));
            }
        }

        result.AddRange(await DeadlinesAsync(from, to, timeZone, token));
        result.AddRange(await TourStopsAsync(from, to, timeZone, categoryById, token));
        result.AddRange(await AbsencesAsync(from, to, timeZone, token));

        IEnumerable<CalendarOccurrence> filtered = result;

        if (!includeCancelled)
        {
            filtered = filtered.Where(o => o.Status != EventStatus.Cancelled);
        }

        if (categories is { Count: > 0 })
        {
            filtered = filtered.Where(o => o.CategoryId is { } id && categories.Contains(id));
        }

        if (resources is { Count: > 0 })
        {
            filtered = filtered.Where(o => o.Resources.Any(r => resources.Contains(r.Id)));
        }

        if (people is { Count: > 0 })
        {
            filtered = filtered.Where(
                o => o.Participants.Any(p => people.Contains(p.ContactId)));
        }

        return [.. filtered.OrderBy(o => o.StartsAt).ThenBy(o => o.Title)];
    }

    /// <summary>
    /// Die Fristen der Aufgaben als Kalendereinträge.
    /// </summary>
    /// <remarks>
    /// Gelesen, nicht kopiert (ADR-0006): Die Frist gehört der Aufgabe. Hier
    /// erscheint sie nur -- ändern lässt sie sich im Aufgabenmodul, und wenn
    /// das abgeschaltet ist, erscheint sie gar nicht.
    ///
    /// Nur Offenes. Was erledigt oder verworfen ist, schränkt die Planung
    /// nicht mehr ein und füllte das Raster mit Vergangenem.
    /// </remarks>
    private async Task<List<CalendarOccurrence>> DeadlinesAsync(
        DateOnly from, DateOnly to, TimeZoneInfo zone, CancellationToken token)
    {
        if (!await TasksEnabledAsync(token))
        {
            return [];
        }

        List<TaskItem> due = await context.TaskItems
            .Include(t => t.Category)
            .Include(t => t.Assignee)
            .Include(t => t.Contacts).ThenInclude(c => c.Contact)
            .Where(t => t.DueDate != null
                        && t.DueDate >= from
                        && t.DueDate <= to
                        && t.State != TaskState.Done
                        && t.State != TaskState.Dropped)
            .AsNoTracking()
            .ToListAsync(token);

        List<CalendarOccurrence> result = [];

        foreach (TaskItem task in due)
        {
            DateOnly day = task.DueDate!.Value;
            DateTimeOffset starts = EventSeries.ToInstant(day, TimeOnly.MinValue, zone);
            DateTimeOffset ends = EventSeries.ToInstant(day, new TimeOnly(23, 59), zone);

            result.Add(new CalendarOccurrence(
                EventId: null,
                SourceId: task.Id,
                SeriesId: null,
                OccurrenceStart: null,
                Title: task.Title,
                Description: task.Description,
                Location: null,
                CategoryId: task.CategoryId,
                CategoryLabel: task.Category?.Label,
                CategoryColor: task.Category?.Color,
                Status: EventStatus.Confirmed,
                StartsAt: starts,
                EndsAt: ends,
                LocalDate: day,
                LocalEndDate: day,
                IsAllDay: true,
                IsFromSeries: false,
                Source: CalendarSource.Tasks,

                // Die Zuständigkeit steht wie eine Beteiligung da, und die
                // Beteiligten ebenso. Dadurch greift der Personenfilter auch
                // auf Fristen: „Was habe ich diese Woche?" umfasst beides.
                Resources: [],
                Participants: [.. Beteiligte(task)]));
        }

        return result;
    }

    private static IEnumerable<OccurrenceParticipant> Beteiligte(TaskItem task)
    {
        if (task.Assignee is { } who)
        {
            yield return new OccurrenceParticipant(
                who.Id, who.Name, null, "Zuständig", ParticipationStatus.Assigned,
                ParticipationStatus.Assigned.Label());
        }

        foreach (TaskContact beteiligt in task.Participants.Where(c => c.Contact is not null && c.ContactId != task.AssigneeId))
        {
            yield return new OccurrenceParticipant(
                beteiligt.ContactId, beteiligt.Contact!.Name, null, "Beteiligt", ParticipationStatus.Assigned,
                ParticipationStatus.Assigned.Label());
        }
    }

    /// <summary>
    /// Die Gastspiele der Tourneen als Kalendereinträge: die Reise als Balken
    /// von Anreise bis Abreise, jede Vorstellung zu ihrer Zeit.
    /// </summary>
    /// <remarks>
    /// Gelesen, nicht kopiert (ADR-0006) -- geändert wird im Touring. Ist das
    /// Modul aus, erscheint nichts; abgesagte Gastspiele erscheinen nie, eine
    /// Anfrage dagegen schon, als Vorbehalt: Der Abend soll nicht zweimal
    /// versprochen werden. Die Reisenden der Tournee stehen als Beteiligte
    /// an jeder Vorstellung -- so greift der Personenfilter, und der
    /// Dienstplan weiß, wer unterwegs ist.
    /// </remarks>
    private async Task<List<CalendarOccurrence>> TourStopsAsync(
        DateOnly from,
        DateOnly to,
        TimeZoneInfo zone,
        IReadOnlyDictionary<Guid, CatalogEntry> categoryById,
        CancellationToken token)
    {
        if (!await ModuleEnabledAsync(SouffleurModule.Touring, token))
        {
            return [];
        }

        DateTimeOffset windowStart = EventSeries.ToInstant(from, TimeOnly.MinValue, zone);
        DateTimeOffset windowEnd = EventSeries.ToInstant(to.AddDays(1), TimeOnly.MinValue, zone);

        List<TourStop> stops = await context.TourStops
            .Include(s => s.Tour).ThenInclude(t => t.Members).ThenInclude(m => m.Contact)
            .Include(s => s.Tour).ThenInclude(t => t.Members).ThenInclude(m => m.Function)
            .Include(s => s.Venue)
            .Include(s => s.Performances)
            .Where(s => s.Status != TourStopStatus.Cancelled)
            .Where(s =>
                (s.ArrivalDate != null && s.DepartureDate != null
                    && s.ArrivalDate <= to && s.DepartureDate >= from)
                || s.Performances.Any(p => p.StartsAt < windowEnd && p.EndsAt > windowStart))
            .AsNoTracking()
            .ToListAsync(token);

        // Die mitgelieferte Terminkategorie „Gastspiel" gibt die Farbe. Hat
        // das Haus sie umbenannt, gilt ihr neuer Name; hat es sie gelöscht --
        // was bei Systemeinträgen nicht geht --, bliebe der Eintrag farblos.
        CatalogEntry? category = categoryById.Values.FirstOrDefault(e => e.Key == "gastspiel");

        List<CalendarOccurrence> result = [];

        foreach (TourStop stop in stops)
        {
            string title = stop.DisplayTitle(stop.Tour.Title);
            EventStatus status = stop.Status.IsBinding() ? EventStatus.Confirmed : EventStatus.Planned;
            IReadOnlyList<OccurrenceParticipant> travellers =
            [
                .. stop.Tour.Members
                    .Where(m => m.Contact is not null)
                    .Select(m => new OccurrenceParticipant(
                        m.ContactId, m.Contact!.Name, m.FunctionId, m.Function?.Label,
                        m.Status, m.Status.Label())),
            ];

            if (stop is { ArrivalDate: { } arrival, DepartureDate: { } departure }
                && arrival <= to && departure >= from)
            {
                result.Add(new CalendarOccurrence(
                    EventId: null,
                    SourceId: stop.Id,
                    SeriesId: null,
                    OccurrenceStart: null,
                    Title: $"Gastspiel: {title}",
                    Description: stop.Notes,
                    Location: stop.Location,
                    CategoryId: category?.Id,
                    CategoryLabel: category?.Label,
                    CategoryColor: category?.Color,
                    Status: status,
                    StartsAt: EventSeries.ToInstant(arrival, TimeOnly.MinValue, zone),
                    EndsAt: EventSeries.ToInstant(departure, new TimeOnly(23, 59), zone),
                    LocalDate: arrival,
                    LocalEndDate: departure,
                    IsAllDay: true,
                    IsFromSeries: false,
                    Source: CalendarSource.Touring,
                    Resources: [],
                    Participants: travellers));
            }

            foreach (TourPerformance performance in stop.Performances
                         .Where(p => p.StartsAt < windowEnd && p.EndsAt > windowStart))
            {
                result.Add(new CalendarOccurrence(
                    EventId: null,
                    SourceId: stop.Id,
                    SeriesId: null,
                    OccurrenceStart: performance.StartsAt,
                    Title: title,
                    Description: performance.Note ?? stop.Notes,
                    Location: stop.Location,
                    CategoryId: category?.Id,
                    CategoryLabel: category?.Label,
                    CategoryColor: category?.Color,
                    Status: status,
                    StartsAt: performance.StartsAt,
                    EndsAt: performance.EndsAt,
                    LocalDate: DateOnly.FromDateTime(
                        TimeZoneInfo.ConvertTime(performance.StartsAt, zone).DateTime),
                    LocalEndDate: DateOnly.FromDateTime(
                        TimeZoneInfo.ConvertTime(performance.EndsAt, zone).DateTime),
                    IsAllDay: false,
                    IsFromSeries: false,
                    Source: CalendarSource.Touring,
                    Resources: [],
                    Participants: travellers));
            }
        }

        return result;
    }

    /// <summary>
    /// Wer in dem Zeitraum fehlt -- genehmigt oder krank gemeldet. Als
    /// ganztaegiger Balken mit der Person als Beteiligter, damit der Filter
    /// nach Personen ihn findet.
    /// </summary>
    /// <remarks>
    /// Der Titel sagt „abwesend" und nie, warum. Der Kalender ist fuer alle im
    /// Haus offen, und dass jemand krank ist, geht die Kollegin am Nebentisch
    /// nichts an -- dass er heute nicht da ist, schon.
    /// </remarks>
    private async Task<List<CalendarOccurrence>> AbsencesAsync(
        DateOnly from,
        DateOnly to,
        TimeZoneInfo zone,
        CancellationToken token)
    {
        if (!await ModuleEnabledAsync(SouffleurModule.Personnel, token))
        {
            return [];
        }

        List<Absence> absences = await context.Absences
            .Include(a => a.Contact)
            .Where(a => (a.Status == AbsenceStatus.Approved || a.Status == AbsenceStatus.Reported)
                        && a.StartDate <= to && a.EndDate >= from)
            .AsNoTracking()
            .ToListAsync(token);

        return
        [
            .. absences.Where(a => a.Contact is not null).Select(a => new CalendarOccurrence(
                EventId: null,
                SourceId: a.Id,
                SeriesId: null,
                OccurrenceStart: null,
                Title: a.IsHalfDay ? $"Halbtags abwesend: {a.Contact!.Name}" : $"Abwesend: {a.Contact!.Name}",
                Description: null,
                Location: null,
                CategoryId: null,
                CategoryLabel: null,
                CategoryColor: null,
                Status: EventStatus.Confirmed,
                StartsAt: EventSeries.ToInstant(a.StartDate, TimeOnly.MinValue, zone),
                EndsAt: EventSeries.ToInstant(a.EndDate, new TimeOnly(23, 59), zone),
                LocalDate: a.StartDate,
                LocalEndDate: a.EndDate,
                IsAllDay: true,
                IsFromSeries: false,
                Source: CalendarSource.Absence,
                Resources: [],
                Participants:
                [
                    new OccurrenceParticipant(
                        a.ContactId, a.Contact.Name, null, null,
                        ParticipationStatus.Declined, "abwesend"),
                ])),
        ];
    }

    /// <summary>
    /// Ob das Aufgabenmodul an ist. Direkt an der Tabelle gefragt und nicht
    /// über den Moduldienst: Der Kalender soll nicht von ihm abhängen, nur
    /// von dem einen Schalter.
    /// </summary>
    private Task<bool> TasksEnabledAsync(CancellationToken token) =>
        ModuleEnabledAsync(SouffleurModule.Workspace, token);

    private async Task<bool> ModuleEnabledAsync(SouffleurModule module, CancellationToken token)
    {
        ModuleSetting? setting = await context.ModuleSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Module == module, token);

        return setting?.IsEnabled ?? module.IsOnByDefault();
    }

    // --- Einzelne Termine ---

    public async Task<CalendarEvent> AddEventAsync(
        CalendarEvent calendarEvent,
        IReadOnlyCollection<Guid>? resourceIds = null,
        IReadOnlyCollection<ParticipantInput>? participants = null,
        bool force = false,
        CancellationToken token = default)
    {
        await ValidateEventAsync(calendarEvent, token);

        List<Guid> rooms = await ResolveResourcesAsync(resourceIds, calendarEvent.Bookings, token);
        List<ParticipantInput> people = await ResolveParticipantsAsync(
            participants, calendarEvent.Participants, token);

        await EnsureFreeAsync(
            rooms, Occupying(people), SpansOf(calendarEvent), null, null, force, token);

        Sync(calendarEvent.Bookings, rooms);
        Sync(calendarEvent.Participants, people);

        context.CalendarEvents.Add(calendarEvent);
        await context.SaveChangesAsync(token);
        return calendarEvent;
    }

    /// <summary>
    /// Wen die Einteilung belegt. Wer abgesagt hat, ist frei -- und soll keinen
    /// Konflikt mit dem auslösen, wofür er stattdessen eingeteilt wird.
    /// </summary>
    private static List<Guid> Occupying(IEnumerable<ParticipantInput> participants) =>
        [.. participants.Where(p => p.Status.Occupies()).Select(p => p.ContactId)];

    public async Task<CalendarEvent> UpdateEventAsync(
        Guid id,
        Action<CalendarEvent> apply,
        IReadOnlyCollection<Guid>? resourceIds = null,
        IReadOnlyCollection<ParticipantInput>? participants = null,
        bool force = false,
        CancellationToken token = default)
    {
        CalendarEvent found = await context.CalendarEvents
            .Include(e => e.Bookings)
            .Include(e => e.Participants)
            .FirstOrDefaultAsync(e => e.Id == id, token)
            ?? throw new CalendarException("Den Termin gibt es nicht.");

        apply(found);
        await ValidateEventAsync(found, token);

        List<Guid> rooms = await ResolveResourcesAsync(resourceIds, found.Bookings, token);
        List<ParticipantInput> people = await ResolveParticipantsAsync(
            participants, found.Participants, token);

        // Sich selbst nicht als Konflikt melden: Wer die Uhrzeit ändert, soll
        // nicht gegen den eigenen alten Stand stoßen.
        await EnsureFreeAsync(
            rooms, Occupying(people), SpansOf(found), found.Id, null, force, token);

        Sync(found.Bookings, rooms);
        Sync(found.Participants, people);

        await context.SaveChangesAsync(token);
        return found;
    }

    /// <summary>
    /// Eine Person an einem bestehenden Termin einteilen.
    /// </summary>
    /// <remarks>
    /// Eigener Weg neben <see cref="UpdateEventAsync"/>, weil der Dienstplan
    /// nur eine Person hinzufuegt und nicht den ganzen Termin kennt. Ueber den
    /// vollen Bogen zu gehen hiesse, alles andere am Termin mitzuschicken --
    /// und was dabei fehlt, waere danach geloescht.
    ///
    /// Geprueft wird nur die neue Person: Wer schon dabei ist, stoesst sonst
    /// gegen den eigenen alten Stand.
    /// </remarks>
    public async Task<CalendarEvent> AssignAsync(
        Guid eventId,
        ParticipantInput person,
        bool force = false,
        CancellationToken token = default)
    {
        CalendarEvent found = await context.CalendarEvents
            .Include(e => e.Participants)
            .FirstOrDefaultAsync(e => e.Id == eventId, token)
            ?? throw new CalendarException("Den Termin gibt es nicht.");

        if (found.Participants.Any(p => p.ContactId == person.ContactId))
        {
            throw new CalendarException("Diese Person ist an diesem Termin schon eingeteilt.");
        }

        if (!await context.Contacts.AnyAsync(c => c.Id == person.ContactId && c.IsActive, token))
        {
            throw new CalendarException("Diesen Kontakt gibt es nicht oder er ist stillgelegt.");
        }

        await EnsureFreeAsync(
            [],
            person.Status.Occupies() ? [person.ContactId] : [],
            SpansOf(found),
            found.Id,
            null,
            force,
            token);

        var assigned = new EventParticipant
        {
            ContactId = person.ContactId,
            FunctionId = person.FunctionId,
            Status = person.Status,
            Note = person.Note,
        };

        found.Participants.Add(assigned);

        // Ausdruecklich hinzufuegen und nicht nur an die Liste haengen:
        // Entity vergibt die Id schon im Konstruktor, und ein Datensatz mit
        // gesetztem Schluessel gilt EF als vorhanden. Daraus wuerde ein UPDATE
        // auf eine Zeile, die es nicht gibt -- und das meldet SQLite als
        // Nebenlaeufigkeitsfehler. Dieselbe Stelle gibt es in Sync().
        context.EventParticipants.Add(assigned);

        await context.SaveChangesAsync(token);
        return found;
    }

    /// <summary>Eine Einteilung wieder loesen.</summary>
    public async Task UnassignAsync(
        Guid eventId,
        Guid contactId,
        CancellationToken token = default)
    {
        CalendarEvent found = await context.CalendarEvents
            .Include(e => e.Participants)
            .FirstOrDefaultAsync(e => e.Id == eventId, token)
            ?? throw new CalendarException("Den Termin gibt es nicht.");

        EventParticipant? person = found.Participants
            .FirstOrDefault(p => p.ContactId == contactId);

        if (person is null)
        {
            return;
        }

        found.Participants.Remove(person);

        // Ausdruecklich loeschen, nicht nur aus der Liste nehmen -- sonst
        // versuchte EF, die Beziehung zu loesen statt die Zeile zu entfernen.
        context.EventParticipants.Remove(person);

        await context.SaveChangesAsync(token);
    }

    public async Task DeleteEventAsync(Guid id, CancellationToken token = default)
    {
        CalendarEvent found = await FindEventAsync(id, token);
        context.CalendarEvents.Remove(found);
        await context.SaveChangesAsync(token);
    }

    // --- Serien ---

    public async Task<EventSeries> AddSeriesAsync(
        EventSeries series,
        IReadOnlyCollection<Guid>? resourceIds = null,
        IReadOnlyCollection<ParticipantInput>? participants = null,
        bool force = false,
        CancellationToken token = default)
    {
        await ValidateSeriesAsync(series, token);

        List<Guid> rooms = await ResolveResourcesAsync(resourceIds, series.Bookings, token);
        List<ParticipantInput> people = await ResolveParticipantsAsync(
            participants, series.Participants, token);

        await EnsureFreeAsync(
            rooms, Occupying(people), SpansOf(series, Today()), null, null, force, token);

        Sync(series.Bookings, rooms);
        Sync(series.Participants, people);

        context.EventSeries.Add(series);
        await context.SaveChangesAsync(token);
        return series;
    }

    public async Task<EventSeries> UpdateSeriesAsync(
        Guid id,
        Action<EventSeries> apply,
        bool keepExceptions = true,
        IReadOnlyCollection<Guid>? resourceIds = null,
        IReadOnlyCollection<ParticipantInput>? participants = null,
        bool force = false,
        CancellationToken token = default)
    {
        EventSeries found = await context.EventSeries
            .Include(s => s.Exceptions)
            .Include(s => s.Bookings)
            .Include(s => s.Participants)
            .FirstOrDefaultAsync(s => s.Id == id, token)
            ?? throw new CalendarException("Die Serie gibt es nicht.");

        apply(found);
        await ValidateSeriesAsync(found, token);

        List<Guid> rooms = await ResolveResourcesAsync(resourceIds, found.Bookings, token);
        List<ParticipantInput> people = await ResolveParticipantsAsync(
            participants, found.Participants, token);

        await EnsureFreeAsync(
            rooms, Occupying(people), SpansOf(found, Today()), null, found.Id, force, token);

        Sync(found.Bookings, rooms);
        Sync(found.Participants, people);

        if (!keepExceptions)
        {
            // Wer die Regel umwirft, kann wollen, dass die Ausnahmen mitgehen.
            // Die Voreinstellung ist das nicht: Eine einzeln verschobene Probe
            // ist eine Entscheidung, die eine Regeländerung nicht kassieren soll.
            context.CalendarEvents.RemoveRange(found.Exceptions);
            found.Exceptions.Clear();
        }

        await context.SaveChangesAsync(token);
        return found;
    }

    public async Task DeleteSeriesAsync(Guid id, CancellationToken token = default)
    {
        EventSeries found = await context.EventSeries.FirstOrDefaultAsync(s => s.Id == id, token)
            ?? throw new CalendarException("Die Serie gibt es nicht.");

        context.EventSeries.Remove(found);
        await context.SaveChangesAsync(token);
    }

    /// <summary>
    /// Ein einzelnes Vorkommen einer Serie abändern -- verschieben, umbenennen
    /// oder absagen. Legt die Abweichung an oder ändert die vorhandene.
    /// </summary>
    public async Task<CalendarEvent> OverrideOccurrenceAsync(
        Guid seriesId,
        DateTimeOffset occurrenceStart,
        Action<CalendarEvent> apply,
        IReadOnlyCollection<Guid>? resourceIds = null,
        IReadOnlyCollection<ParticipantInput>? participants = null,
        bool force = false,
        CancellationToken token = default)
    {
        EventSeries series = await context.EventSeries
            .Include(s => s.Bookings)
            .Include(s => s.Participants)
            .FirstOrDefaultAsync(s => s.Id == seriesId, token)
            ?? throw new CalendarException("Die Serie gibt es nicht.");

        CalendarEvent? exception = await context.CalendarEvents
            .Include(e => e.Bookings)
            .Include(e => e.Participants)
            .FirstOrDefaultAsync(
                e => e.SeriesId == seriesId && e.OccurrenceStart == occurrenceStart, token);

        if (exception is null)
        {
            // Die Vorlage kommt aus der Serie, damit die Abweichung für sich
            // steht: Spätere Regeländerungen sollen den geänderten Termin nicht
            // nachträglich wieder mitziehen.
            exception = new CalendarEvent
            {
                SeriesId = seriesId,
                OccurrenceStart = occurrenceStart,
                Title = series.Title,
                Description = series.Description,
                Location = series.Location,
                CategoryId = series.CategoryId,
                Status = series.Status,
                IsAllDay = series.IsAllDay,
                StartsAt = occurrenceStart,
                EndsAt = occurrenceStart + TimeSpan.FromMinutes(Math.Max(1, series.DurationMinutes)),
            };

            // Die Belegung der Serie geht mit: Dieser eine Termin findet
            // weiterhin auf derselben Bühne statt, bis jemand etwas anderes
            // sagt. Ohne das gäbe die verschobene Probe den Saal lautlos frei.
            foreach (ResourceBooking booking in series.Bookings)
            {
                exception.Bookings.Add(new ResourceBooking { ResourceId = booking.ResourceId });
            }

            // Und die Einteilung ebenso: Wer für die Serie eingeteilt ist, ist
            // auch an diesem einen Abend eingeteilt, bis jemand es ändert.
            foreach (EventParticipant person in series.Participants)
            {
                exception.Participants.Add(new EventParticipant
                {
                    ContactId = person.ContactId,
                    FunctionId = person.FunctionId,
                    Status = person.Status,
                    Note = person.Note,
                });
            }

            context.CalendarEvents.Add(exception);
        }

        apply(exception);
        await ValidateEventAsync(exception, token);

        List<Guid> rooms = await ResolveResourcesAsync(resourceIds, exception.Bookings, token);
        List<ParticipantInput> people = await ResolveParticipantsAsync(
            participants, exception.Participants, token);

        await EnsureFreeAsync(
            rooms, Occupying(people), SpansOf(exception), exception.Id, seriesId, force, token);

        Sync(exception.Bookings, rooms);
        Sync(exception.Participants, people);

        await context.SaveChangesAsync(token);
        return exception;
    }

    public Task<CalendarEvent> CancelOccurrenceAsync(
        Guid seriesId, DateTimeOffset occurrenceStart, CancellationToken token = default) =>
        OverrideOccurrenceAsync(
            seriesId,
            occurrenceStart,
            e => e.Status = EventStatus.Cancelled,
            token: token);

    /// <summary>
    /// Die Abweichung zurücknehmen: Das Vorkommen folgt wieder der Regel.
    /// </summary>
    public async Task ResetOccurrenceAsync(
        Guid seriesId, DateTimeOffset occurrenceStart, CancellationToken token = default)
    {
        CalendarEvent? exception = await context.CalendarEvents
            .FirstOrDefaultAsync(
                e => e.SeriesId == seriesId && e.OccurrenceStart == occurrenceStart, token);

        if (exception is not null)
        {
            context.CalendarEvents.Remove(exception);
            await context.SaveChangesAsync(token);
        }
    }

    // --- Belegung: Räume und Leute ---

    /// <summary>
    /// Wie weit die Belegungsprüfung einer Serie nach vorn schaut.
    /// </summary>
    /// <remarks>
    /// Eine Serie ohne Ende belegt die Bühne für immer; irgendwo muss die
    /// Prüfung aufhören. Gut ein Jahr deckt die Disposition ab, in der ein
    /// Konflikt sich noch auflösen lässt.
    /// </remarks>
    public const int ConflictHorizonDays = 400;

    /// <summary>
    /// Mehr Überschneidungen meldet der Server nicht. Wer dreihundert davon
    /// hat, braucht keine Liste, sondern eine andere Entscheidung.
    /// </summary>
    public const int MaxConflictsReported = 50;

    /// <summary>Ein belegter Zeitabschnitt.</summary>
    public readonly record struct Span(DateTimeOffset Start, DateTimeOffset End)
    {
        /// <remarks>
        /// Berührung ist keine Überschneidung: Die Probe, die um 22:00 endet,
        /// steht der Vorstellung um 22:00 nicht im Weg.
        /// </remarks>
        public bool Overlaps(Span other) => Start < other.End && other.Start < End;
    }

    /// <summary>Die Zeit, die ein einzelner Termin belegt.</summary>
    public static IReadOnlyList<Span> SpansOf(CalendarEvent item) =>
        item.Status == EventStatus.Cancelled ? [] : [new Span(item.StartsAt, item.EndsAt)];

    /// <summary>
    /// Die Zeiten, die eine Serie belegt -- ab heute und bis zum Horizont.
    /// </summary>
    /// <remarks>
    /// Geprüft wird nach vorn, nicht in die Vergangenheit: Eine Doppelbelegung
    /// vom letzten Jahr lässt sich nicht mehr auflösen, und sie zu melden
    /// hielte nur davon ab, die Regel zu ändern.
    /// </remarks>
    public static IReadOnlyList<Span> SpansOf(EventSeries rule, DateOnly today)
    {
        if (rule.Status == EventStatus.Cancelled)
        {
            return [];
        }

        DateOnly from = rule.StartDate > today ? rule.StartDate : today;
        DateOnly to = from.AddDays(ConflictHorizonDays);

        if (rule.Until is { } until && until < to)
        {
            to = until;
        }

        if (to < from)
        {
            return [];
        }

        TimeZoneInfo zone = rule.Zone();
        var duration = TimeSpan.FromMinutes(Math.Max(1, rule.DurationMinutes));

        return
        [
            .. rule.LocalDates(from, to).Select(date =>
            {
                DateTimeOffset starts = EventSeries.ToInstant(date, rule.StartTime, zone);
                return new Span(starts, starts + duration);
            }),
        ];
    }

    /// <summary>Wer oder was belegt ist, mit Namen für die Meldung.</summary>
    private sealed record Subject(ConflictSubject Kind, Guid Id, string Name);

    /// <summary>Dass ein Termin oder eine Serie ein Subjekt belegt.</summary>
    private sealed record Link(Guid SubjectId, Guid? EventId, Guid? SeriesId);

    /// <summary>
    /// Was den gewünschten Räumen und Leuten in diesen Zeiten im Weg steht.
    /// </summary>
    /// <remarks>
    /// Räume und Personen durch dieselbe Prüfung: Eine Schauspielerin lässt
    /// sich so wenig zweimal vergeben wie ein Saal, und die Disposition will
    /// beides in derselben Meldung sehen.
    ///
    /// Bei Räumen zählt nur, was exklusiv ist -- ein Scheinwerferpark lässt
    /// sich teilen. Bei Personen gibt es das nicht. Abgesagtes zählt nicht,
    /// Vorgemerktes schon: Eine Option auf einen Gastspieltermin blockiert den
    /// Saal, sonst wäre sie keine.
    /// </remarks>
    public async Task<List<ScheduleConflict>> ConflictsAsync(
        IReadOnlyCollection<Guid> resourceIds,
        IReadOnlyCollection<Guid> contactIds,
        IReadOnlyList<Span> spans,
        Guid? ignoreEventId = null,
        Guid? ignoreSeriesId = null,
        CancellationToken token = default)
    {
        if (spans.Count == 0 || (resourceIds.Count == 0 && contactIds.Count == 0))
        {
            return [];
        }

        Guid[] wantedResources = [.. resourceIds.Distinct()];
        Guid[] wantedContacts = [.. contactIds.Distinct()];

        Dictionary<Guid, Subject> subjects = [];
        List<Link> links = [];

        if (wantedResources.Length > 0)
        {
            foreach (var resource in await context.Resources
                         .Where(r => wantedResources.Contains(r.Id) && r.IsExclusive)
                         .Select(r => new { r.Id, r.Name })
                         .AsNoTracking()
                         .ToListAsync(token))
            {
                subjects[resource.Id] = new Subject(
                    ConflictSubject.Resource, resource.Id, resource.Name);
            }

            Guid[] exclusive = [.. subjects.Keys];
            links.AddRange(await context.ResourceBookings
                .Where(b => exclusive.Contains(b.ResourceId))
                .Select(b => new Link(b.ResourceId, b.EventId, b.SeriesId))
                .AsNoTracking()
                .ToListAsync(token));
        }

        if (wantedContacts.Length > 0)
        {
            foreach (var contact in await context.Contacts
                         .Where(c => wantedContacts.Contains(c.Id))
                         .Select(c => new { c.Id, c.Name })
                         .AsNoTracking()
                         .ToListAsync(token))
            {
                subjects[contact.Id] = new Subject(
                    ConflictSubject.Person, contact.Id, contact.Name);
            }

            links.AddRange(await context.EventParticipants
                .Where(p => wantedContacts.Contains(p.ContactId)
                            && p.Status != ParticipationStatus.Declined)
                .Select(p => new Link(p.ContactId, p.EventId, p.SeriesId))
                .AsNoTracking()
                .ToListAsync(token));
        }

        List<ScheduleConflict> away = await AbsenceConflictsAsync(
            [.. subjects.Values.Where(s => s.Kind == ConflictSubject.Person)], spans, token);

        if (subjects.Count == 0 || links.Count == 0)
        {
            return away;
        }

        DateTimeOffset windowStart = spans.Min(s => s.Start);
        DateTimeOffset windowEnd = spans.Max(s => s.End);

        ILookup<Guid, Guid> ofEvent = links
            .Where(l => l.EventId is not null)
            .ToLookup(l => l.EventId!.Value, l => l.SubjectId);

        ILookup<Guid, Guid> ofSeries = links
            .Where(l => l.SeriesId is not null)
            .ToLookup(l => l.SeriesId!.Value, l => l.SubjectId);

        Guid[] otherEvents = [.. ofEvent.Select(g => g.Key).Where(id => id != ignoreEventId)];
        Guid[] otherSeries = [.. ofSeries.Select(g => g.Key).Where(id => id != ignoreSeriesId)];

        List<CalendarEvent> events = await context.CalendarEvents
            .Where(e => otherEvents.Contains(e.Id)
                        && e.Status != EventStatus.Cancelled
                        && e.StartsAt < windowEnd
                        && e.EndsAt > windowStart
                        && (ignoreSeriesId == null || e.SeriesId != ignoreSeriesId))
            .AsNoTracking()
            .ToListAsync(token);

        List<EventSeries> rules = await context.EventSeries
            .Where(r => otherSeries.Contains(r.Id) && r.Status != EventStatus.Cancelled)
            .AsNoTracking()
            .ToListAsync(token);

        // Überschriebene Vorkommen fallen weg -- an ihrer Stelle steht der
        // eigene Datensatz, der oben schon geladen ist.
        List<Guid> ruleIds = [.. rules.Select(r => r.Id)];
        var overrides = await context.CalendarEvents
            .Where(e => e.SeriesId != null
                        && ruleIds.Contains(e.SeriesId!.Value)
                        && e.OccurrenceStart != null)
            .Select(e => new { Series = e.SeriesId!.Value, Occurrence = e.OccurrenceStart!.Value })
            .AsNoTracking()
            .ToListAsync(token);

        HashSet<(Guid Series, DateTime Occurrence)> overridden =
            [.. overrides.Select(x => (x.Series, x.Occurrence.UtcDateTime))];

        TimeZoneInfo timeZone = await ZoneAsync(token);
        DateOnly from = LocalDay(windowStart, timeZone).AddDays(-1);
        DateOnly to = LocalDay(windowEnd, timeZone).AddDays(1);

        List<ScheduleConflict> conflicts = [];

        void Compare(
            IEnumerable<Guid> occupied,
            Span span,
            string title,
            Guid? eventId,
            Guid? seriesId,
            DateTimeOffset? occurrenceStart,
            EventStatus status)
        {
            if (conflicts.Count >= MaxConflictsReported || !spans.Any(span.Overlaps))
            {
                return;
            }

            foreach (Guid id in occupied)
            {
                Subject subject = subjects[id];
                conflicts.Add(new ScheduleConflict(
                    subject.Kind,
                    subject.Id,
                    subject.Name,
                    LocalDay(span.Start, timeZone),
                    span.Start,
                    span.End,
                    title,
                    eventId,
                    seriesId,
                    occurrenceStart,
                    status));
            }
        }

        foreach (CalendarEvent other in events)
        {
            Compare(
                ofEvent[other.Id].Distinct().Where(subjects.ContainsKey),
                new Span(other.StartsAt, other.EndsAt),
                other.Title,
                other.Id,
                other.SeriesId,
                other.OccurrenceStart,
                other.Status);
        }

        foreach (EventSeries rule in rules)
        {
            var duration = TimeSpan.FromMinutes(Math.Max(1, rule.DurationMinutes));
            TimeZoneInfo ruleZone = rule.Zone();

            foreach (DateOnly date in rule.LocalDates(from, to))
            {
                DateTimeOffset starts = EventSeries.ToInstant(date, rule.StartTime, ruleZone);

                if (overridden.Contains((rule.Id, starts.UtcDateTime)))
                {
                    continue;
                }

                Compare(
                    ofSeries[rule.Id].Distinct().Where(subjects.ContainsKey),
                    new Span(starts, starts + duration),
                    rule.Title,
                    null,
                    rule.Id,
                    starts,
                    rule.Status);
            }
        }

        return [.. conflicts.Concat(away)
                            .OrderBy(c => c.StartsAt).ThenBy(c => c.SubjectName)
                            .Take(MaxConflictsReported)];
    }

    /// <summary>
    /// Wer in den Zeiten nicht da ist -- genehmigt, krank gemeldet oder
    /// beantragt. Ein beantragter Urlaub ist noch keiner, aber wer einteilt,
    /// soll davon wissen, bevor er den Abend verspricht.
    /// </summary>
    /// <remarks>
    /// Wie im Kalender ohne Grund: Die Meldung sieht, wer einteilt, und das
    /// ist nicht unbedingt jemand, der Krankmeldungen sehen darf.
    /// </remarks>
    private async Task<List<ScheduleConflict>> AbsenceConflictsAsync(
        IReadOnlyList<Subject> people,
        IReadOnlyList<Span> spans,
        CancellationToken token)
    {
        if (people.Count == 0 || !await ModuleEnabledAsync(SouffleurModule.Personnel, token))
        {
            return [];
        }

        TimeZoneInfo zone = await ZoneAsync(token);
        DateOnly from = LocalDay(spans.Min(s => s.Start), zone);
        DateOnly to = LocalDay(spans.Max(s => s.End), zone);
        Guid[] ids = [.. people.Select(p => p.Id)];

        List<Absence> absences = await context.Absences
            .Where(a => ids.Contains(a.ContactId)
                        && (a.Status == AbsenceStatus.Approved
                            || a.Status == AbsenceStatus.Reported
                            || a.Status == AbsenceStatus.Requested)
                        && a.StartDate <= to && a.EndDate >= from)
            .AsNoTracking()
            .ToListAsync(token);

        List<ScheduleConflict> conflicts = [];

        foreach (Absence absence in absences)
        {
            var span = new Span(
                EventSeries.ToInstant(absence.StartDate, TimeOnly.MinValue, zone),
                EventSeries.ToInstant(absence.EndDate.AddDays(1), TimeOnly.MinValue, zone));

            if (!spans.Any(span.Overlaps))
            {
                continue;
            }

            Subject person = people.First(p => p.Id == absence.ContactId);
            conflicts.Add(new ScheduleConflict(
                ConflictSubject.Person,
                person.Id,
                person.Name,
                absence.StartDate,
                span.Start,
                span.End,
                absence.Status == AbsenceStatus.Requested ? "Abwesenheit beantragt" : "Abwesend",
                null,
                null,
                null,
                absence.Status == AbsenceStatus.Requested ? EventStatus.Planned : EventStatus.Confirmed));
        }

        return conflicts;
    }

    /// <summary>
    /// Die gewünschten Ressourcen prüfen. <c>null</c> heißt: unverändert
    /// lassen, was schon dranhängt -- nicht: alles abräumen.
    /// </summary>
    private async Task<List<Guid>> ResolveResourcesAsync(
        IReadOnlyCollection<Guid>? resourceIds,
        List<ResourceBooking> current,
        CancellationToken token)
    {
        if (resourceIds is null)
        {
            return [.. current.Select(b => b.ResourceId)];
        }

        Guid[] wanted = [.. resourceIds.Distinct()];
        if (wanted.Length == 0)
        {
            return [];
        }

        List<Resource> found = await context.Resources
            .Where(r => wanted.Contains(r.Id))
            .AsNoTracking()
            .ToListAsync(token);

        List<string> problems = [];
        HashSet<Guid> alreadyThere = [.. current.Select(b => b.ResourceId)];

        foreach (Guid id in wanted)
        {
            Resource? resource = found.FirstOrDefault(r => r.Id == id);

            if (resource is null)
            {
                problems.Add("Eine der gewählten Ressourcen gibt es nicht.");
            }
            else if (!resource.IsActive && !alreadyThere.Contains(id))
            {
                // Abgeschaltet heißt: nicht mehr wählbar. Wer sie schon trägt,
                // behält sie -- sonst ließe sich der Termin nicht mehr ändern.
                problems.Add($"Die Ressource „{resource.Name}“ ist abgeschaltet.");
            }
        }

        if (problems.Count > 0)
        {
            throw new CalendarException(problems);
        }

        return [.. wanted];
    }

    /// <summary>
    /// Die gewünschte Einteilung prüfen. <c>null</c> heißt auch hier:
    /// unverändert lassen.
    /// </summary>
    private async Task<List<ParticipantInput>> ResolveParticipantsAsync(
        IReadOnlyCollection<ParticipantInput>? wanted,
        List<EventParticipant> current,
        CancellationToken token)
    {
        if (wanted is null)
        {
            return
            [
                .. current.Select(p =>
                    new ParticipantInput(p.ContactId, p.FunctionId, p.Status, p.Note)),
            ];
        }

        // Dieselbe Person zweimal am selben Termin ist keine Einteilung,
        // sondern ein Versehen -- die letzte Angabe gilt.
        List<ParticipantInput> distinct =
            [.. wanted.GroupBy(p => p.ContactId).Select(group => group.Last())];

        if (distinct.Count == 0)
        {
            return [];
        }

        Guid[] ids = [.. distinct.Select(p => p.ContactId)];
        List<Contact> contacts = await context.Contacts
            .Where(c => ids.Contains(c.Id))
            .AsNoTracking()
            .ToListAsync(token);

        Guid[] functions = [.. distinct.Where(p => p.FunctionId is not null)
                                       .Select(p => p.FunctionId!.Value).Distinct()];

        List<CatalogEntry> known = functions.Length == 0
            ? []
            : await context.CatalogEntries
                .Where(e => functions.Contains(e.Id)
                            && e.Catalog == CatalogKind.ParticipationRole)
                .AsNoTracking()
                .ToListAsync(token);

        List<string> problems = [];
        HashSet<Guid> alreadyThere = [.. current.Select(p => p.ContactId)];

        foreach (ParticipantInput participant in distinct)
        {
            Contact? contact = contacts.FirstOrDefault(c => c.Id == participant.ContactId);

            if (contact is null)
            {
                problems.Add("Einen der gewählten Kontakte gibt es nicht.");
            }
            else if (!contact.IsActive && !alreadyThere.Contains(contact.Id))
            {
                problems.Add($"„{contact.Name}“ ist im Adressbuch abgeschaltet.");
            }

            if (participant.FunctionId is { } functionId
                && known.All(entry => entry.Id != functionId))
            {
                problems.Add("Die gewählte Funktion gibt es nicht.");
            }
        }

        if (problems.Count > 0)
        {
            throw new CalendarException(problems);
        }

        return distinct;
    }

    private async Task EnsureFreeAsync(
        IReadOnlyCollection<Guid> resourceIds,
        IReadOnlyCollection<Guid> contactIds,
        IReadOnlyList<Span> spans,
        Guid? ignoreEventId,
        Guid? ignoreSeriesId,
        bool force,
        CancellationToken token)
    {
        if (force)
        {
            // Übergehbar mit Absicht: Die Disposition weiß manchmal, dass sie
            // doppelt belegt, und löst es später auf. Verschwiegen wird es
            // trotzdem nicht -- der Aufrufer musste es ausdrücklich sagen.
            return;
        }

        List<ScheduleConflict> conflicts = await ConflictsAsync(
            resourceIds, contactIds, spans, ignoreEventId, ignoreSeriesId, token);

        if (conflicts.Count > 0)
        {
            throw new ScheduleConflictException(conflicts);
        }
    }

    /// <summary>Die Belegungen auf den gewünschten Stand bringen.</summary>
    /// <remarks>
    /// Zwei Umwege, die beide nötig sind, weil die Listen an einem schon
    /// geladenen Termin hängen:
    ///
    /// Was wegfällt, wird ausdrücklich gelöscht und nicht nur aus der Liste
    /// genommen. Der Fremdschlüssel ist beidseitig optional -- entweder Termin
    /// oder Serie --, und EF Core löst eine solche Verknüpfung durch Nullen
    /// des Schlüssels statt durch Löschen. Übrig bliebe eine Zeile, die zu
    /// nichts gehört, und die Prüfbedingung in der Datenbank schlägt zu.
    ///
    /// Was hinzukommt, wird ausdrücklich angelegt. Die Schlüssel entstehen im
    /// Code (<c>Entity.Id</c>), nicht in der Datenbank; EF Core hält einen neu
    /// angehängten Datensatz mit gesetztem Schlüssel deshalb für einen
    /// bestehenden und schickt ein UPDATE, das keine Zeile trifft.
    /// </remarks>
    private void Sync(List<ResourceBooking> bookings, IReadOnlyCollection<Guid> wanted)
    {
        List<ResourceBooking> gone = [.. bookings.Where(b => !wanted.Contains(b.ResourceId))];

        foreach (ResourceBooking booking in gone)
        {
            bookings.Remove(booking);
        }

        context.ResourceBookings.RemoveRange(gone);

        foreach (Guid id in wanted.Where(id => bookings.All(b => b.ResourceId != id)))
        {
            var booking = new ResourceBooking { ResourceId = id };
            bookings.Add(booking);
            context.ResourceBookings.Add(booking);
        }
    }

    /// <summary>
    /// Die Einteilung auf den gewünschten Stand bringen.
    /// </summary>
    /// <remarks>
    /// Wer bleibt, behält seinen Datensatz: Sonst verlöre eine Änderung an der
    /// Funktion die Zusage, die daneben steht.
    /// </remarks>
    private void Sync(
        List<EventParticipant> participants, IReadOnlyCollection<ParticipantInput> wanted)
    {
        // Ausdrücklich löschen, nicht nur aus der Liste nehmen -- siehe oben.
        List<EventParticipant> gone =
            [.. participants.Where(p => wanted.All(w => w.ContactId != p.ContactId))];

        foreach (EventParticipant person in gone)
        {
            participants.Remove(person);
        }

        context.EventParticipants.RemoveRange(gone);

        foreach (ParticipantInput input in wanted)
        {
            EventParticipant? existing =
                participants.FirstOrDefault(p => p.ContactId == input.ContactId);

            if (existing is null)
            {
                var person = new EventParticipant
                {
                    ContactId = input.ContactId,
                    FunctionId = input.FunctionId,
                    Status = input.Status,
                    Note = input.Note,
                };

                participants.Add(person);
                context.EventParticipants.Add(person);
            }
            else
            {
                existing.FunctionId = input.FunctionId;
                existing.Status = input.Status;
                existing.Note = input.Note;
            }
        }
    }

    /// <summary>
    /// Was an den Terminen und Serien hängt: Ressourcen und Eingeteilte.
    /// </summary>
    private async Task<Attachments> AttachmentsOfAsync(
        IReadOnlyCollection<Guid> eventIds,
        IReadOnlyCollection<Guid> seriesIds,
        CancellationToken token)
    {
        var result = new Attachments();

        if (eventIds.Count == 0 && seriesIds.Count == 0)
        {
            return result;
        }

        Guid[] events = [.. eventIds];
        Guid[] series = [.. seriesIds];

        var bookings = await context.ResourceBookings
            .Where(b => (b.EventId != null && events.Contains(b.EventId.Value))
                        || (b.SeriesId != null && series.Contains(b.SeriesId.Value)))
            .Select(b => new
            {
                b.EventId,
                b.SeriesId,
                b.ResourceId,
                b.Resource!.Name,
                b.Resource.ShortName,
                Color = b.Resource.Kind != null ? b.Resource.Kind.Color : null,
            })
            .AsNoTracking()
            .ToListAsync(token);

        foreach (var booking in bookings)
        {
            Add(
                booking.EventId is not null ? result.ResourcesByEvent : result.ResourcesBySeries,
                booking.EventId ?? booking.SeriesId!.Value,
                new OccurrenceResource(
                    booking.ResourceId,
                    booking.Name,
                    string.IsNullOrWhiteSpace(booking.ShortName) ? booking.Name : booking.ShortName,
                    booking.Color));
        }

        var people = await context.EventParticipants
            .Where(p => (p.EventId != null && events.Contains(p.EventId.Value))
                        || (p.SeriesId != null && series.Contains(p.SeriesId.Value)))
            .Select(p => new
            {
                p.EventId,
                p.SeriesId,
                p.ContactId,
                p.Contact!.Name,
                p.FunctionId,
                Function = p.Function != null ? p.Function.Label : null,
                p.Status,
            })
            .AsNoTracking()
            .ToListAsync(token);

        foreach (var person in people)
        {
            Add(
                person.EventId is not null
                    ? result.PeopleByEvent
                    : result.PeopleBySeries,
                person.EventId ?? person.SeriesId!.Value,
                new OccurrenceParticipant(
                    person.ContactId,
                    person.Name,
                    person.FunctionId,
                    person.Function,
                    person.Status,
                    person.Status.Label()));
        }

        result.Sort();
        return result;
    }

    private static void Add<T>(Dictionary<Guid, List<T>> map, Guid key, T value)
    {
        if (map.TryGetValue(key, out List<T>? list))
        {
            list.Add(value);
        }
        else
        {
            map[key] = [value];
        }
    }

    private static IReadOnlyList<T> Taken<T>(
        IReadOnlyDictionary<Guid, List<T>> map, Guid id) =>
        map.TryGetValue(id, out List<T>? found) ? found : [];

    private static DateOnly LocalDay(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>Was an Terminen und Serien hängt, nach Herkunft getrennt.</summary>
    private sealed class Attachments
    {
        public Dictionary<Guid, List<OccurrenceResource>> ResourcesByEvent { get; } = [];

        public Dictionary<Guid, List<OccurrenceResource>> ResourcesBySeries { get; } = [];

        public Dictionary<Guid, List<OccurrenceParticipant>> PeopleByEvent { get; } = [];

        public Dictionary<Guid, List<OccurrenceParticipant>> PeopleBySeries { get; } = [];

        /// <summary>
        /// Nach Namen, damit dieselbe Einteilung immer gleich dasteht -- sonst
        /// springt die Reihenfolge mit jedem Abruf.
        /// </summary>
        public void Sort()
        {
            foreach (List<OccurrenceResource> list in
                     ResourcesByEvent.Values.Concat(ResourcesBySeries.Values))
            {
                list.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
            }

            foreach (List<OccurrenceParticipant> list in
                     PeopleByEvent.Values.Concat(PeopleBySeries.Values))
            {
                list.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
            }
        }
    }

    // --- Prüfungen ---

    private async Task<CalendarEvent> FindEventAsync(Guid id, CancellationToken token) =>
        await context.CalendarEvents.FirstOrDefaultAsync(e => e.Id == id, token)
        ?? throw new CalendarException("Den Termin gibt es nicht.");

    private async Task ValidateEventAsync(CalendarEvent item, CancellationToken token)
    {
        List<string> problems = [];

        if (string.IsNullOrWhiteSpace(item.Title))
        {
            problems.Add("Ein Titel ist nötig.");
        }

        if (item.EndsAt < item.StartsAt)
        {
            problems.Add("Der Termin endet vor seinem Anfang.");
        }

        await CheckCategoryAsync(item.CategoryId, problems, token);
        await CheckProductionAsync(item.ProductionId, problems, token);

        if (problems.Count > 0)
        {
            throw new CalendarException(problems);
        }
    }

    private async Task ValidateSeriesAsync(EventSeries series, CancellationToken token)
    {
        List<string> problems = [];

        if (string.IsNullOrWhiteSpace(series.Title))
        {
            problems.Add("Ein Titel ist nötig.");
        }

        if (series.Interval < 1)
        {
            problems.Add("Der Abstand muss mindestens 1 sein.");
        }

        if (series.DurationMinutes < 1)
        {
            problems.Add("Die Dauer muss mindestens eine Minute betragen.");
        }

        if (series.Until is { } until && until < series.StartDate)
        {
            problems.Add("Die Serie endet vor ihrem ersten Termin.");
        }

        if (series.Count is { } count && count < 1)
        {
            problems.Add("Eine Serie hat mindestens einen Termin.");
        }

        if (series.Until is not null && series.Count is not null)
        {
            problems.Add("Entweder ein Enddatum oder eine Anzahl -- nicht beides.");
        }

        if (series.Frequency == RecurrenceFrequency.Monthly)
        {
            if (series.BySetPosition is { } position && position is 0 or < -1 or > 4)
            {
                problems.Add("Das Vorkommen im Monat ist 1 bis 4 oder −1 für das letzte.");
            }

            if (series.ByMonthDay is { } day && day is < 1 or > 31)
            {
                problems.Add("Der Tag im Monat liegt zwischen 1 und 31.");
            }

            if (series.BySetPosition is not null && series.ByMonthDay is not null)
            {
                problems.Add("Entweder ein Tag im Monat oder ein Wochentag-Vorkommen.");
            }
        }

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(series.TimeZoneId);
        }
        catch (Exception exception) when (
            exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            problems.Add($"Die Zeitzone „{series.TimeZoneId}“ ist unbekannt.");
        }

        await CheckCategoryAsync(series.CategoryId, problems, token);
        await CheckProductionAsync(series.ProductionId, problems, token);

        if (problems.Count > 0)
        {
            throw new CalendarException(problems);
        }
    }

    private async Task CheckProductionAsync(
        Guid? productionId, List<string> problems, CancellationToken token)
    {
        if (productionId is { } id && !await context.Productions.AnyAsync(p => p.Id == id, token))
        {
            problems.Add("Diese Produktion gibt es nicht.");
        }
    }

    private async Task CheckCategoryAsync(
        Guid? categoryId, List<string> problems, CancellationToken token)
    {
        if (categoryId is not { } id)
        {
            return;
        }

        CatalogEntry? category = await context.CatalogEntries
            .FirstOrDefaultAsync(e => e.Id == id && e.Catalog == CatalogKind.EventCategory, token);

        if (category is null)
        {
            problems.Add("Die Kategorie gibt es nicht.");
        }
        else if (!category.IsActive)
        {
            // Abgeschaltet heißt: nicht mehr auswählbar. An Terminen, die sie
            // schon tragen, bleibt sie lesbar -- deshalb nur hier geprüft.
            problems.Add($"Die Kategorie „{category.Label}“ ist abgeschaltet.");
        }
    }

    private static CalendarOccurrence ToOccurrence(
        CalendarEvent stored,
        TimeZoneInfo timeZone,
        IReadOnlyDictionary<Guid, CatalogEntry> categories,
        IReadOnlyList<OccurrenceResource> resources,
        IReadOnlyList<OccurrenceParticipant> participants) => new(
        EventId: stored.Id,
        SourceId: stored.Id,
        SeriesId: stored.SeriesId,
        OccurrenceStart: stored.OccurrenceStart,
        Title: stored.Title,
        Description: stored.Description,
        Location: stored.Location,
        CategoryId: stored.CategoryId,
        CategoryLabel: Label(stored.CategoryId, categories),
        CategoryColor: Color(stored.CategoryId, categories),
        Status: stored.Status,
        StartsAt: stored.StartsAt,
        EndsAt: stored.EndsAt,
        LocalDate: DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(stored.StartsAt, timeZone).DateTime),
        LocalEndDate: DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(stored.EndsAt, timeZone).DateTime),
        IsAllDay: stored.IsAllDay,
        IsFromSeries: stored.SeriesId is not null,
        Source: CalendarSource.Calendar,
        Resources: resources,
        Participants: participants);

    private static string? Label(Guid? id, IReadOnlyDictionary<Guid, CatalogEntry> categories) =>
        id is { } found && categories.TryGetValue(found, out CatalogEntry? entry) ? entry.Label : null;

    private static string? Color(Guid? id, IReadOnlyDictionary<Guid, CatalogEntry> categories) =>
        id is { } found && categories.TryGetValue(found, out CatalogEntry? entry) ? entry.Color : null;
}
