using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Souffleur.Api.Infrastructure;
using Souffleur.Domain.Bookmarks;
using Souffleur.Domain.Modules;
using Souffleur.Infrastructure.Bookmarks;
using Souffleur.Domain.Work;
using Souffleur.Infrastructure.Work;
using Souffleur.Infrastructure.Identity;
using Souffleur.Infrastructure.Persistence;
using Souffleur.Api.Features.Notifications;
using Souffleur.Infrastructure.Notifications;

namespace Souffleur.Api.Features.Work;

/// <summary>
/// Die Aufgaben des Hauses -- siehe ADR-0007.
/// </summary>
/// <remarks>
/// Schreiben darf jeder Angemeldete, nicht nur die Leitung. Eine Aufgabenliste,
/// in die nur wenige eintragen dürfen, wird nicht benutzt -- dann steht das
/// Eigentliche wieder auf Zetteln.
/// </remarks>
public static class TaskEndpoints
{
    public static IEndpointRouteBuilder MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/tasks")
            .WithTags("Aufgaben")
            .RequireModule(SouffleurModule.Workspace)
            .RequireAuthorization();

        group.MapGet("/", ListAsync);
        group.MapGet("/states", ListStates);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost("/", CreateAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapPost("/{id:guid}/state", SetStateAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);
        group.MapPost("/order", ReorderAsync);
        group.MapGet("/timeline", TimelineAsync);
        group.MapPost("/{id:guid}/depends-on/{other:guid}", LinkAsync);
        group.MapDelete("/{id:guid}/depends-on/{other:guid}", UnlinkAsync);

        return app;
    }

    /// <summary>
    /// Der Zeitstrahl: Vorgänge mit Dauer, nicht Tage mit Terminen.
    /// </summary>
    private static async Task<IResult> TimelineAsync(
        TaskService tasks,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] bool includeClosed = false,
        CancellationToken token = default)
    {
        Timeline timeline = await tasks.TimelineAsync(from, to, includeClosed, token);
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

        return TypedResults.Ok(new TimelineResponse(
            timeline.From,
            timeline.To,
            [.. timeline.Tasks.Select(task => TimelineEntry.Of(task, today))],
            [.. timeline.Dependencies.Select(d => new DependencyResponse(d.TaskId, d.DependsOnId))],
            [.. timeline.Breaches.Select(b => new BreachResponse(
                b.TaskId, b.TaskTitle, b.DependsOnId, b.DependsOnTitle))],
            timeline.Unscheduled));
    }

    private static async Task<IResult> LinkAsync(
        Guid id, Guid other, TaskService tasks, CancellationToken token)
    {
        try
        {
            await tasks.LinkAsync(id, other, token);
        }
        catch (TaskException exception)
        {
            return Rejected(exception);
        }

        return TypedResults.NoContent();
    }

    private static async Task<IResult> UnlinkAsync(
        Guid id, Guid other, TaskService tasks, CancellationToken token)
    {
        await tasks.UnlinkAsync(id, other, token);
        return TypedResults.NoContent();
    }

    private static IResult ListStates() =>
        TypedResults.Ok(TaskStates.All.Select(state => new TaskStateInfo(
            state, state.ToString(), state.Label(), state.IsOpen())));

    private static async Task<IResult> ListAsync(
        TaskService tasks,
        [FromQuery] TaskState[]? states = null,
        [FromQuery] Guid? category = null,
        [FromQuery] Guid? assignee = null,
        [FromQuery] bool includeClosed = false,
        [FromQuery] bool meineGruppen = false,
        [FromQuery] Guid? production = null,
        AccessService? access = null,
        ICurrentUser? currentUser = null,
        SouffleurDbContext? context = null,
        BookmarkService? bookmarks = null,
        CancellationToken token = default)
    {
        // „Fuer meine Gruppen": was an einer Gruppe haengt, in der ich bin.
        IReadOnlyCollection<Guid>? groupIds = null;
        if (meineGruppen && access is not null && currentUser?.Id is { } userId)
        {
            groupIds = [.. (await access.GroupsOfAsync(userId, token)).Select(g => g.Id)];
        }

        List<TaskItem> found = await tasks.ListAsync(
            states, category, assignee, includeClosed, groupIds, production, token);

        // Wie viel an jeder Aufgabe haengt -- fuer die Bueroklammer in der
        // Zeile. Nur gezaehlt, nicht geladen; die Dokumente selbst holt der
        // Bogen ueber die Ablage, die weiss, wer was sehen darf.
        IReadOnlyDictionary<Guid, int> anhaenge = await AnhaengeAsync(found, context!, bookmarks!, token);

        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        return TypedResults.Ok(found.Select(task =>
            TaskResponse.From(task, today, anhaenge.GetValueOrDefault(task.Id))));
    }

    private static async Task<IReadOnlyDictionary<Guid, int>> AnhaengeAsync(
        List<TaskItem> found, SouffleurDbContext context, BookmarkService bookmarks, CancellationToken token)
    {
        List<Guid> ids = [.. found.Select(t => t.Id)];
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var dokumente = await context.DocumentLinks
            .Where(l => l.Target == Domain.Documents.DocumentTarget.Task && ids.Contains(l.TargetId))
            .GroupBy(l => l.TargetId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(token);
        IReadOnlyDictionary<Guid, int> lesezeichen = await bookmarks.CountAsync(BookmarkTarget.Task, ids, token);

        Dictionary<Guid, int> summe = dokumente.ToDictionary(d => d.Key, d => d.Count);
        foreach ((Guid id, int count) in lesezeichen)
        {
            summe[id] = summe.GetValueOrDefault(id) + count;
        }

        return summe;
    }

    private static async Task<IResult> GetAsync(
        Guid id, TaskService tasks, CancellationToken token) =>
        await tasks.FindAsync(id, token) is { } found
            ? TypedResults.Ok(TaskResponse.From(found, DateOnly.FromDateTime(DateTime.UtcNow)))
            : TypedResults.NotFound();

    private static async Task<IResult> CreateAsync(
        [FromBody] TaskRequest request, TaskService tasks, NotificationService glocke, CancellationToken token)
    {
        var task = new TaskItem { Title = request.Title.Trim() };
        Apply(request, task, tasks);

        try
        {
            await tasks.AddAsync(task, token);
        }
        catch (TaskException exception)
        {
            return Rejected(exception);
        }

        TaskItem created = await tasks.FindAsync(task.Id, token) ?? task;
        await Ausloeser.AufgabeAsync(glocke, created, vorherZustaendig: null, vorherGruppe: null, vorherBeteiligt: null, token: token);
        return TypedResults.Created(
            $"/api/tasks/{task.Id}",
            TaskResponse.From(created, DateOnly.FromDateTime(DateTime.UtcNow)));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, [FromBody] TaskRequest request, TaskService tasks, NotificationService glocke, CancellationToken token)
    {
        // Wie es vorher war -- benachrichtigt wird nur, wer neu dazukommt.
        TaskItem? vorher = await tasks.FindAsync(id, token);

        try
        {
            TaskItem updated = await tasks.UpdateAsync(id, task =>
            {
                task.Title = request.Title.Trim();
                Apply(request, task, tasks);
            }, token);

            TaskItem reloaded = await tasks.FindAsync(updated.Id, token) ?? updated;
            IReadOnlyCollection<Guid> vorherBeteiligt = [.. (vorher?.Participants ?? []).Select(p => p.ContactId)];
            await Ausloeser.AufgabeAsync(glocke, reloaded, vorher?.AssigneeId, vorher?.GroupId, vorherBeteiligt, token);
            return TypedResults.Ok(
                TaskResponse.From(reloaded, DateOnly.FromDateTime(DateTime.UtcNow)));
        }
        catch (TaskException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> SetStateAsync(
        Guid id, [FromBody] TaskStateRequest request, TaskService tasks, CancellationToken token)
    {
        try
        {
            TaskItem updated = await tasks.SetStateAsync(id, request.State, token);
            TaskItem reloaded = await tasks.FindAsync(updated.Id, token) ?? updated;
            return TypedResults.Ok(
                TaskResponse.From(reloaded, DateOnly.FromDateTime(DateTime.UtcNow)));
        }
        catch (TaskException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> DeleteAsync(
        Guid id, TaskService tasks, BookmarkService bookmarks, CancellationToken token)
    {
        try
        {
            await tasks.DeleteAsync(id, token);
        }
        catch (TaskException exception)
        {
            return Rejected(exception);
        }

        // Die Lesezeichen kennen die Aufgabe nur ueber ihre Id -- ohne das
        // hier blieben sie als Waisen liegen.
        await bookmarks.RemoveAllAsync(BookmarkTarget.Task, id, token);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ReorderAsync(
        [FromBody] IReadOnlyList<Guid> order, TaskService tasks, CancellationToken token)
    {
        await tasks.ReorderAsync(order, token);
        return TypedResults.NoContent();
    }

    private static void Apply(TaskRequest request, TaskItem task, TaskService tasks)
    {
        task.Description = Trimmed(request.Description);
        task.CategoryId = request.CategoryId;
        task.AssigneeId = request.AssigneeId;
        task.GroupId = request.GroupId;
        task.ProductionId = request.ProductionId;
        task.CostCenterId = request.CostCenterId;
        task.DueDate = request.DueDate;
        task.StartDate = request.StartDate;
        task.ParentId = request.ParentId;
        task.State = request.State;
        tasks.SetContacts(task, request.ParticipantIds ?? [], request.ContactIds ?? []);
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IResult Rejected(TaskException exception) =>
        TypedResults.ValidationProblem(
            new Dictionary<string, string[]> { ["Aufgabe"] = [.. exception.Problems] },
            title: "Die Aufgabe wurde nicht angenommen.");
}

public record TaskStateInfo(TaskState State, string Key, string Label, bool IsOpen);

public record TimelineResponse(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<TimelineEntry> Tasks,
    IReadOnlyList<DependencyResponse> Dependencies,
    IReadOnlyList<BreachResponse> Breaches,
    int Unscheduled);

public record DependencyResponse(Guid TaskId, Guid DependsOnId);

public record BreachResponse(
    Guid TaskId, string TaskTitle, Guid DependsOnId, string DependsOnTitle);

/// <param name="IsMilestone">
/// Ein Stichtag ohne Dauer -- die Ansicht zeichnet eine Raute statt eines
/// Balkens.
/// </param>
public record TimelineEntry(
    Guid Id,
    string Title,
    DateOnly From,
    DateOnly To,
    bool IsMilestone,
    Guid? CategoryId,
    string? CategoryLabel,
    string? CategoryColor,
    string? AssigneeName,
    TaskState State,
    string StateLabel,
    bool IsOpen,
    bool IsOverdue,
    Guid? ParentId)
{
    /// <remarks>
    /// Heißt <c>Of</c> und nicht <c>From</c>: Der Balken hat schon ein Feld
    /// dieses Namens, und zwei Bedeutungen an einem Wort sind eine zu viel.
    /// </remarks>
    public static TimelineEntry Of(TaskItem t, DateOnly today)
    {
        (DateOnly from, DateOnly to) = t.Span!.Value;

        return new TimelineEntry(
            t.Id, t.Title, from, to, from == to, t.CategoryId, t.Category?.Label,
            t.Category?.Color, t.Assignee?.Name, t.State, t.State.Label(), t.IsOpen,
            t.IsOverdue(today), t.ParentId);
    }
}

/// <param name="ParticipantIds">Beteiligte -- Kontakte, die mitarbeiten, ohne die Aufgabe zu tragen.</param>
/// <param name="ContactIds">Verknuepfte Kontakte -- kommen darin vor, tun aber nichts.</param>
public record TaskRequest(
    string Title,
    string? Description = null,
    Guid? CategoryId = null,
    Guid? AssigneeId = null,
    DateOnly? DueDate = null,
    DateOnly? StartDate = null,
    Guid? ParentId = null,
    TaskState State = TaskState.Open,
    Guid? GroupId = null,
    Guid? ProductionId = null,
    Guid? CostCenterId = null,
    IReadOnlyList<Guid>? ParticipantIds = null,
    IReadOnlyList<Guid>? ContactIds = null);

public record TaskContactResponse(Guid Id, string Name);

public record TaskStateRequest(TaskState State);

public record TaskResponse(
    Guid Id,
    string Title,
    string? Description,
    Guid? CategoryId,
    string? CategoryLabel,
    string? CategoryColor,
    Guid? AssigneeId,
    string? AssigneeName,
    Guid? GroupId,
    string? GroupName,
    Guid? ProductionId,
    string? ProductionTitle,
    Guid? CostCenterId,
    string? CostCenterLabel,
    IReadOnlyList<TaskContactResponse> Participants,
    IReadOnlyList<TaskContactResponse> Contacts,
    TaskState State,
    string StateLabel,
    bool IsOpen,
    bool IsOverdue,
    DateOnly? DueDate,
    DateOnly? StartDate,
    Guid? ParentId,
    int SortOrder,
    DateTimeOffset? ClosedAt,
    int AttachmentCount)
{
    /// <param name="attachments">Dokumente und Lesezeichen zusammen -- nur eine Zahl fuer die Zeile.</param>
    public static TaskResponse From(TaskItem t, DateOnly today, int attachments = 0) => new(
        t.Id, t.Title, t.Description, t.CategoryId, t.Category?.Label, t.Category?.Color,
        t.AssigneeId, t.Assignee?.Name, t.GroupId, t.Group?.Name, t.ProductionId, t.Production?.Title,
        t.CostCenterId, t.CostCenter?.Label,
        [.. t.Participants.Where(c => c.Contact is not null).Select(c => new TaskContactResponse(c.ContactId, c.Contact!.Name))],
        [.. t.RelatedContacts.Where(c => c.Contact is not null).Select(c => new TaskContactResponse(c.ContactId, c.Contact!.Name))],
        t.State, t.State.Label(), t.IsOpen,
        t.IsOverdue(today), t.DueDate, t.StartDate, t.ParentId, t.SortOrder, t.ClosedAt, attachments);
}
