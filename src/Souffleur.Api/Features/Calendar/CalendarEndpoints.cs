using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Souffleur.Api.Infrastructure;
using Souffleur.Domain.Calendar;
using Souffleur.Domain.Identity;
using Souffleur.Domain.Modules;
using Microsoft.EntityFrameworkCore;
using Souffleur.Infrastructure.Calendar;
using Souffleur.Infrastructure.Persistence;
using Souffleur.Infrastructure.Identity;
using Souffleur.Api.Features.Notifications;
using Souffleur.Infrastructure.Notifications;


namespace Souffleur.Api.Features.Calendar;

/// <summary>
/// Termine, Serien und das Jahresraster -- siehe ADR-0006.
/// </summary>
public static class CalendarEndpoints
{
    public static IEndpointRouteBuilder MapCalendarEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/calendar")
            .WithTags("Kalender")
            .RequireModule(SouffleurModule.Calendar);

        group.MapGet("/range", RangeAsync).RequireAuthorization();
        group.MapGet("/events/{id:guid}", GetEventAsync).RequireAuthorization();
        group.MapGet("/series/{id:guid}", GetSeriesAsync).RequireAuthorization();

        // Vorab fragen, ob die Bühne frei ist -- bevor jemand auf „Anlegen“
        // drückt und erst dann erfährt, dass sie belegt ist.
        group.MapPost("/conflicts", ConflictsAsync).RequireAuthorization(Plan);

        group.MapPost("/events", CreateEventAsync).RequireAuthorization(Plan);
        group.MapPut("/events/{id:guid}", UpdateEventAsync).RequireAuthorization(Plan);
        group.MapDelete("/events/{id:guid}", DeleteEventAsync).RequireAuthorization(Plan);

        // Eine einzelne Einteilung an einem bestehenden Termin. Eigener Weg
        // neben dem vollen Bogen, damit der Dienstplan jemanden besetzen kann,
        // ohne den ganzen Termin mitzuschicken.
        group.MapPost("/events/{id:guid}/participants", AssignAsync)
             .RequireAuthorization(Plan);
        group.MapDelete("/events/{id:guid}/participants/{contactId:guid}", UnassignAsync)
             .RequireAuthorization(Plan);

        group.MapPost("/series", CreateSeriesAsync).RequireAuthorization(Plan);
        group.MapPut("/series/{id:guid}", UpdateSeriesAsync).RequireAuthorization(Plan);
        group.MapDelete("/series/{id:guid}", DeleteSeriesAsync).RequireAuthorization(Plan);

        // Ein einzelnes Vorkommen einer Serie. Der Zeitpunkt im Pfad ist der
        // von der Regel errechnete, nicht der neue -- nur so ist das Vorkommen
        // wiederzufinden, nachdem es verschoben wurde.
        group.MapPut("/series/{id:guid}/occurrences/{start}", OverrideOccurrenceAsync)
             .RequireAuthorization(Plan);
        group.MapPost("/series/{id:guid}/occurrences/{start}/cancel", CancelOccurrenceAsync)
             .RequireAuthorization(Plan);
        group.MapDelete("/series/{id:guid}/occurrences/{start}", ResetOccurrenceAsync)
             .RequireAuthorization(Plan);

        return app;
    }

    private static void Plan(AuthorizationPolicyBuilder policy) =>
        policy.RequireClaim(PermissionClaimsFactory.ClaimType, ((int)Permission.CalendarPlan).ToString());

    private static async Task<IResult> RangeAsync(
        CalendarService calendar,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] bool includeCancelled = true,
        [FromQuery] Guid[]? categories = null,
        [FromQuery] Guid[]? resources = null,
        [FromQuery] Guid[]? people = null,
        CancellationToken token = default)
    {
        try
        {
            List<CalendarOccurrence> found = await calendar.RangeAsync(
                from, to, includeCancelled, categories, resources, people, token);

            TimeZoneInfo zone = await calendar.ZoneAsync(token);

            return TypedResults.Ok(new RangeResponse(
                from, to, zone.Id, [.. found.Select(OccurrenceResponse.From)]));
        }
        catch (CalendarException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> GetEventAsync(
        Guid id, SouffleurDbContext context, CancellationToken token) =>
        // Mit Einteilung: Der Bogen zum Bearbeiten schickt sie vollstaendig
        // zurueck. Fehlte sie hier, loeschte jedes Speichern alle Eingeteilten.
        await context.CalendarEvents.Include(e => e.Bookings).Include(e => e.Participants).AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, token) is { } found
            ? TypedResults.Ok(EventResponse.From(found))
            : TypedResults.NotFound();

    private static async Task<IResult> GetSeriesAsync(
        Guid id, SouffleurDbContext context, CancellationToken token) =>
        await context.EventSeries.Include(s => s.Bookings).Include(s => s.Participants).AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, token) is { } found
            ? TypedResults.Ok(SeriesResponse.From(found))
            : TypedResults.NotFound();

    /// <summary>
    /// Ob die gewünschten Ressourcen frei sind -- ohne etwas anzulegen.
    /// </summary>
    private static async Task<IResult> ConflictsAsync(
        [FromBody] ConflictRequest request, CalendarService calendar, CancellationToken token)
    {
        IReadOnlyList<CalendarService.Span> spans;

        if (request.Series is { } rule)
        {
            var series = new EventSeries
            {
                Title = rule.Title,
                StartDate = rule.StartDate,
                StartTime = rule.StartTime,
            };
            Apply(rule, series);
            spans = CalendarService.SpansOf(series, DateOnly.FromDateTime(DateTime.UtcNow));
        }
        else if (request.StartsAt is { } starts && request.EndsAt is { } ends)
        {
            spans = [new CalendarService.Span(starts, ends)];
        }
        else
        {
            return TypedResults.BadRequest(
                "Entweder ein Zeitraum oder eine Serienregel ist nötig.");
        }

        List<ScheduleConflict> conflicts = await calendar.ConflictsAsync(
            request.ResourceIds,
            request.ContactIds ?? [],
            spans,
            request.IgnoreEventId,
            request.IgnoreSeriesId,
            token);

        return TypedResults.Ok(new ConflictResponse(
            [.. conflicts.Select(ConflictItem.From)]));
    }

    private static async Task<IResult> CreateEventAsync(
        [FromBody] EventRequest request, CalendarService calendar, NotificationService glocke, CancellationToken token)
    {
        var item = new CalendarEvent
        {
            Title = request.Title,
            StartsAt = request.StartsAt,
            EndsAt = request.EndsAt,
        };
        Apply(request, item);

        try
        {
            await calendar.AddEventAsync(
                item, request.ResourceIds, Participants(request), request.Force, token);
        }
        catch (CalendarException exception)
        {
            return Rejected(exception);
        }
        catch (ScheduleConflictException conflict)
        {
            return Occupied(conflict);
        }

        if (item.Participants.Count > 0)
        {
            await Ausloeser.DienstAsync(
                glocke, item.Participants.Select(p => p.ContactId), eingeteilt: true, item.Title, item.StartsAt, token);
        }

        return TypedResults.Created($"/api/calendar/events/{item.Id}", EventResponse.From(item));
    }

    private static async Task<IResult> UpdateEventAsync(
        Guid id,
        [FromBody] EventRequest request,
        CalendarService calendar,
        NotificationService glocke,
        SouffleurDbContext context,
        CancellationToken token)
    {
        // Der Stand vorher: Wer war eingeteilt, und wann begann es?
        var vorher = await context.CalendarEvents.AsNoTracking()
            .Where(e => e.Id == id)
            .Select(e => new { e.StartsAt, Kontakte = e.Participants.Select(p => p.ContactId).ToList() })
            .FirstOrDefaultAsync(token);

        try
        {
            CalendarEvent updated = await calendar.UpdateEventAsync(id, item =>
            {
                item.Title = request.Title;
                item.StartsAt = request.StartsAt;
                item.EndsAt = request.EndsAt;
                Apply(request, item);
            }, request.ResourceIds, Participants(request), request.Force, token);

            if (vorher is not null)
            {
                HashSet<Guid> jetzt = [.. updated.Participants.Select(p => p.ContactId)];
                HashSet<Guid> davor = [.. vorher.Kontakte];

                await Ausloeser.DienstAsync(
                    glocke, jetzt.Except(davor), eingeteilt: true, updated.Title, updated.StartsAt, token);
                await Ausloeser.DienstAsync(
                    glocke, davor.Except(jetzt), eingeteilt: false, updated.Title, updated.StartsAt, token);

                if (vorher.StartsAt != updated.StartsAt)
                {
                    await Ausloeser.VerschobenAsync(
                        glocke, jetzt.Intersect(davor), updated.Title, updated.StartsAt, token);
                }
            }

            return TypedResults.Ok(EventResponse.From(updated));
        }
        catch (CalendarException exception)
        {
            return Rejected(exception);
        }
        catch (ScheduleConflictException conflict)
        {
            return Occupied(conflict);
        }
    }

    private static async Task<IResult> DeleteEventAsync(
        Guid id, CalendarService calendar, CancellationToken token)
    {
        try
        {
            await calendar.DeleteEventAsync(id, token);
        }
        catch (CalendarException exception)
        {
            return Rejected(exception);
        }

        return TypedResults.NoContent();
    }

    private static async Task<IResult> CreateSeriesAsync(
        [FromBody] SeriesRequest request, CalendarService calendar, CancellationToken token)
    {
        var series = new EventSeries
        {
            Title = request.Title,
            StartDate = request.StartDate,
            StartTime = request.StartTime,
        };
        Apply(request, series);

        try
        {
            await calendar.AddSeriesAsync(
                series, request.ResourceIds, Participants(request), request.Force, token);
        }
        catch (CalendarException exception)
        {
            return Rejected(exception);
        }
        catch (ScheduleConflictException conflict)
        {
            return Occupied(conflict);
        }

        return TypedResults.Created($"/api/calendar/series/{series.Id}", SeriesResponse.From(series));
    }

    private static async Task<IResult> UpdateSeriesAsync(
        Guid id,
        [FromBody] SeriesRequest request,
        CalendarService calendar,
        [FromQuery] bool keepExceptions = true,
        CancellationToken token = default)
    {
        try
        {
            EventSeries updated = await calendar.UpdateSeriesAsync(id, series =>
            {
                series.Title = request.Title;
                series.StartDate = request.StartDate;
                series.StartTime = request.StartTime;
                Apply(request, series);
            }, keepExceptions, request.ResourceIds, Participants(request), request.Force, token);

            return TypedResults.Ok(SeriesResponse.From(updated));
        }
        catch (CalendarException exception)
        {
            return Rejected(exception);
        }
        catch (ScheduleConflictException conflict)
        {
            return Occupied(conflict);
        }
    }

    private static async Task<IResult> DeleteSeriesAsync(
        Guid id, CalendarService calendar, CancellationToken token)
    {
        try
        {
            await calendar.DeleteSeriesAsync(id, token);
        }
        catch (CalendarException exception)
        {
            return Rejected(exception);
        }

        return TypedResults.NoContent();
    }

    private static async Task<IResult> OverrideOccurrenceAsync(
        Guid id,
        DateTimeOffset start,
        [FromBody] EventRequest request,
        CalendarService calendar,
        CancellationToken token)
    {
        try
        {
            CalendarEvent exception = await calendar.OverrideOccurrenceAsync(id, start, item =>
            {
                item.Title = request.Title;
                item.StartsAt = request.StartsAt;
                item.EndsAt = request.EndsAt;
                Apply(request, item);
            }, request.ResourceIds, Participants(request), request.Force, token);

            return TypedResults.Ok(EventResponse.From(exception));
        }
        catch (CalendarException failure)
        {
            return Rejected(failure);
        }
        catch (ScheduleConflictException conflict)
        {
            return Occupied(conflict);
        }
    }

    private static async Task<IResult> CancelOccurrenceAsync(
        Guid id, DateTimeOffset start, CalendarService calendar, CancellationToken token)
    {
        try
        {
            CalendarEvent cancelled = await calendar.CancelOccurrenceAsync(id, start, token);
            return TypedResults.Ok(EventResponse.From(cancelled));
        }
        catch (CalendarException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> ResetOccurrenceAsync(
        Guid id, DateTimeOffset start, CalendarService calendar, CancellationToken token)
    {
        await calendar.ResetOccurrenceAsync(id, start, token);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Die gewünschte Einteilung aus der Anfrage. <c>null</c> heißt: unverändert
    /// lassen -- sonst räumte jede Änderung am Titel die Eingeteilten ab.
    /// </summary>
    private static IReadOnlyList<ParticipantInput>? Participants(
        IReadOnlyList<ParticipantRequest>? requested) =>
        requested is null
            ? null
            : [.. requested.Select(p => new ParticipantInput(
                p.ContactId, p.FunctionId, p.Status, p.Note))];

    private static IReadOnlyList<ParticipantInput>? Participants(EventRequest request) =>
        Participants(request.Participants);

    private static IReadOnlyList<ParticipantInput>? Participants(SeriesRequest request) =>
        Participants(request.Participants);

    private static void Apply(EventRequest request, CalendarEvent item)
    {
        item.Description = request.Description;
        item.Location = request.Location;
        item.CategoryId = request.CategoryId;
        item.ProductionId = request.ProductionId;
        item.Status = request.Status;
        item.IsAllDay = request.IsAllDay;
    }

    private static void Apply(SeriesRequest request, EventSeries series)
    {
        series.Description = request.Description;
        series.Location = request.Location;
        series.CategoryId = request.CategoryId;
        series.ProductionId = request.ProductionId;
        series.Status = request.Status;
        series.IsAllDay = request.IsAllDay;
        series.DurationMinutes = request.DurationMinutes;
        series.TimeZoneId = request.TimeZoneId ?? series.TimeZoneId;
        series.Frequency = request.Frequency;
        series.Interval = request.Interval;
        series.ByWeekday = request.ByWeekday;
        series.ByMonthDay = request.ByMonthDay;
        series.BySetPosition = request.BySetPosition;
        series.Until = request.Until;
        series.Count = request.Count;
    }

    private static async Task<IResult> AssignAsync(
        Guid id,
        [FromBody] AssignRequest request,
        CalendarService calendar,
        NotificationService glocke,
        CancellationToken token)
    {
        try
        {
            CalendarEvent termin = await calendar.AssignAsync(
                id,
                new ParticipantInput(request.ContactId, request.FunctionId, request.Status),
                request.Force,
                token);

            await Ausloeser.DienstAsync(
                glocke, [request.ContactId], eingeteilt: true, termin.Title, termin.StartsAt, token);
            return TypedResults.NoContent();
        }
        catch (ScheduleConflictException conflict)
        {
            return Occupied(conflict);
        }
        catch (CalendarException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> UnassignAsync(
        Guid id,
        Guid contactId,
        CalendarService calendar,
        NotificationService glocke,
        SouffleurDbContext context,
        CancellationToken token)
    {
        var termin = await context.CalendarEvents.AsNoTracking()
            .Where(e => e.Id == id)
            .Select(e => new { e.Title, e.StartsAt })
            .FirstOrDefaultAsync(token);

        try
        {
            await calendar.UnassignAsync(id, contactId, token);
            if (termin is not null)
            {
                await Ausloeser.DienstAsync(glocke, [contactId], eingeteilt: false, termin.Title, termin.StartsAt, token);
            }

            return TypedResults.NoContent();
        }
        catch (CalendarException exception)
        {
            return Rejected(exception);
        }
    }

    /// <summary>
    /// 409 und nicht 400: Die Eingabe ist in Ordnung, die Welt steht im Weg.
    /// Mit <c>force</c> lässt sich derselbe Aufruf durchsetzen.
    /// </summary>
    private static IResult Occupied(ScheduleConflictException conflict) =>
        TypedResults.Conflict(new ConflictResponse(
            [.. conflict.Conflicts.Select(ConflictItem.From)]));

    private static IResult Rejected(CalendarException exception) =>
        TypedResults.ValidationProblem(
            new Dictionary<string, string[]> { ["Termin"] = [.. exception.Problems] },
            title: "Der Termin wurde nicht angenommen.");
}

public record AssignRequest(
    Guid ContactId,
    Guid? FunctionId = null,
    ParticipationStatus Status = ParticipationStatus.Assigned,
    bool Force = false);

public record RangeResponse(
    DateOnly From,
    DateOnly To,
    string TimeZone,
    IReadOnlyList<OccurrenceResponse> Occurrences);

public record OccurrenceResponse(
    Guid? EventId,
    Guid? SourceId,
    Guid? SeriesId,
    DateTimeOffset? OccurrenceStart,
    string Title,
    string? Description,
    string? Location,
    Guid? CategoryId,
    string? CategoryLabel,
    string? CategoryColor,
    EventStatus Status,
    string StatusLabel,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateOnly LocalDate,
    DateOnly LocalEndDate,
    int SpanDays,
    bool IsAllDay,
    bool IsFromSeries,
    CalendarSource Source,
    IReadOnlyList<OccurrenceResource> Resources,
    IReadOnlyList<OccurrenceParticipant> Participants)
{
    public static OccurrenceResponse From(CalendarOccurrence o) => new(
        o.EventId, o.SourceId, o.SeriesId, o.OccurrenceStart, o.Title, o.Description, o.Location,
        o.CategoryId, o.CategoryLabel, o.CategoryColor, o.Status, o.Status.Label(),
        o.StartsAt, o.EndsAt, o.LocalDate, o.LocalEndDate, o.SpanDays, o.IsAllDay,
        o.IsFromSeries, o.Source, o.Resources, o.Participants);
}

/// <param name="ResourceIds">
/// Was der Termin belegt. Leer heißt: nichts. Fehlt das Feld ganz, bleibt die
/// Belegung, wie sie war -- sonst räumte jede Änderung am Titel die Bühne frei.
/// </param>
/// <param name="Force">
/// Trotz Doppelbelegung anlegen. Die Disposition weiß manchmal, dass sie
/// doppelt belegt, und löst es später auf.
/// </param>
public record EventRequest(
    string Title,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? Description = null,
    string? Location = null,
    Guid? CategoryId = null,
    EventStatus Status = EventStatus.Confirmed,
    bool IsAllDay = false,
    IReadOnlyList<Guid>? ResourceIds = null,
    IReadOnlyList<ParticipantRequest>? Participants = null,
    bool Force = false,
    Guid? ProductionId = null);

/// <param name="FunctionId">Was die Person an diesem Termin tut -- ein Katalogeintrag.</param>
public record ParticipantRequest(
    Guid ContactId,
    Guid? FunctionId = null,
    ParticipationStatus Status = ParticipationStatus.Assigned,
    string? Note = null);

public record EventResponse(
    Guid Id,
    Guid? SeriesId,
    DateTimeOffset? OccurrenceStart,
    string Title,
    string? Description,
    string? Location,
    Guid? CategoryId,
    EventStatus Status,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    bool IsAllDay,
    IReadOnlyList<Guid> ResourceIds,
    IReadOnlyList<ParticipantRequest> Participants,
    Guid? ProductionId = null)
{
    public static EventResponse From(CalendarEvent e) => new(
        e.Id, e.SeriesId, e.OccurrenceStart, e.Title, e.Description, e.Location,
        e.CategoryId, e.Status, e.StartsAt, e.EndsAt, e.IsAllDay,
        [.. e.Bookings.Select(b => b.ResourceId)],
        [.. e.Participants.Select(p =>
            new ParticipantRequest(p.ContactId, p.FunctionId, p.Status, p.Note))],
        e.ProductionId);
}

public record SeriesRequest(
    string Title,
    DateOnly StartDate,
    TimeOnly StartTime,
    int DurationMinutes = 60,
    string? Description = null,
    string? Location = null,
    Guid? CategoryId = null,
    EventStatus Status = EventStatus.Confirmed,
    bool IsAllDay = false,
    string? TimeZoneId = null,
    RecurrenceFrequency Frequency = RecurrenceFrequency.Weekly,
    int Interval = 1,
    Weekdays ByWeekday = Weekdays.None,
    int? ByMonthDay = null,
    int? BySetPosition = null,
    DateOnly? Until = null,
    int? Count = null,
    IReadOnlyList<Guid>? ResourceIds = null,
    IReadOnlyList<ParticipantRequest>? Participants = null,
    bool Force = false,
    Guid? ProductionId = null);

public record SeriesResponse(
    Guid Id,
    string Title,
    string? Description,
    string? Location,
    Guid? CategoryId,
    EventStatus Status,
    DateOnly StartDate,
    TimeOnly StartTime,
    int DurationMinutes,
    string TimeZoneId,
    RecurrenceFrequency Frequency,
    int Interval,
    Weekdays ByWeekday,
    int? ByMonthDay,
    int? BySetPosition,
    DateOnly? Until,
    int? Count,
    bool IsAllDay,
    string Rule,
    IReadOnlyList<Guid> ResourceIds,
    IReadOnlyList<ParticipantRequest> Participants,
    Guid? ProductionId = null)
{
    public static SeriesResponse From(EventSeries s) => new(
        s.Id, s.Title, s.Description, s.Location, s.CategoryId, s.Status, s.StartDate,
        s.StartTime, s.DurationMinutes, s.TimeZoneId, s.Frequency, s.Interval, s.ByWeekday,
        s.ByMonthDay, s.BySetPosition, s.Until, s.Count, s.IsAllDay, s.Describe(),
        [.. s.Bookings.Select(b => b.ResourceId)],
        [.. s.Participants.Select(p =>
            new ParticipantRequest(p.ContactId, p.FunctionId, p.Status, p.Note))],
        s.ProductionId);
}

/// <summary>
/// Die Vorabfrage: Sind diese Ressourcen zu dieser Zeit frei?
/// </summary>
/// <param name="Series">
/// Statt eines Zeitraums eine Serienregel -- dann wird jeder ihrer Termine
/// bis zum Horizont geprüft.
/// </param>
public record ConflictRequest(
    IReadOnlyList<Guid> ResourceIds,
    IReadOnlyList<Guid>? ContactIds = null,
    DateTimeOffset? StartsAt = null,
    DateTimeOffset? EndsAt = null,
    Guid? IgnoreEventId = null,
    Guid? IgnoreSeriesId = null,
    SeriesRequest? Series = null);

public record ConflictResponse(IReadOnlyList<ConflictItem> Conflicts);

/// <param name="Subject">Ob ein Raum oder eine Person belegt ist.</param>
public record ConflictItem(
    ConflictSubject Subject,
    Guid SubjectId,
    string SubjectName,
    DateOnly LocalDate,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Title,
    Guid? EventId,
    Guid? SeriesId,
    DateTimeOffset? OccurrenceStart,
    EventStatus Status,
    string StatusLabel)
{
    public static ConflictItem From(ScheduleConflict c) => new(
        c.Subject, c.SubjectId, c.SubjectName, c.LocalDate, c.StartsAt, c.EndsAt, c.Title,
        c.EventId, c.SeriesId, c.OccurrenceStart, c.Status, c.Status.Label());
}
