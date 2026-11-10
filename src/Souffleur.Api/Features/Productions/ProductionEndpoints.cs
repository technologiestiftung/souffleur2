using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Souffleur.Api.Infrastructure;
using Souffleur.Domain.Accounting;
using Souffleur.Domain.Calendar;
using Souffleur.Domain.Documents;
using Souffleur.Domain.Identity;
using Souffleur.Domain.Modules;
using Souffleur.Domain.Productions;
using Souffleur.Domain.Touring;
using Souffleur.Domain.Work;
using Souffleur.Infrastructure.Documents;
using Souffleur.Infrastructure.Identity;
using Souffleur.Infrastructure.Persistence;
using Souffleur.Infrastructure.Productions;

namespace Souffleur.Api.Features.Productions;

/// <summary>
/// Die Produktionen des Hauses und was an ihnen haengt.
/// </summary>
/// <remarks>
/// Lesen darf jeder Angemeldete -- wer im Ensemble steht, will wissen, wann
/// Premiere ist. Anlegen, aendern und abspielen darf, wer Produktionen
/// verwaltet. Was die Uebersicht aus den anderen Modulen zeigt, folgt deren
/// Rechten: Belege nur mit Finanzrecht, Dokumente nur die sichtbaren.
/// </remarks>
public static class ProductionEndpoints
{
    public static IEndpointRouteBuilder MapProductionEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/productions")
            .WithTags("Produktionen")
            .RequireModule(SouffleurModule.Production)
            .RequireAuthorization();

        group.MapGet("/statuses", ListStatuses);
        group.MapGet("/", ListAsync);
        group.MapGet("/castings/{contactId:guid}", CastingsAsync);
        group.MapGet("/{id:guid}", GetAsync);

        RouteGroupBuilder planning = group.MapGroup("/")
            .RequirePermission(Permission.ProductionsManage);

        planning.MapPost("/", CreateAsync);
        planning.MapPut("/{id:guid}", UpdateAsync);
        planning.MapDelete("/{id:guid}", DeleteAsync);

        // Die Vorgaben abschnittsweise: Wer die Besetzung pflegt, schickt
        // nur die Besetzung -- und ueberschreibt nicht nebenbei das Budget.
        planning.MapPut("/{id:guid}/roles", RolesAsync);
        planning.MapPut("/{id:guid}/budget", BudgetAsync);
        planning.MapPut("/{id:guid}/needs", NeedsAsync);

        // Vom Kontakt aus: eine Rolle dazu, eine Rolle weg.
        planning.MapPost("/{id:guid}/roles", AddRoleAsync);
        planning.MapDelete("/{id:guid}/roles/{roleId:guid}", RemoveRoleAsync);

        // Belege nachtraeglich zuordnen -- auch festgeschriebene. Dafuer
        // braucht es beides: die Produktion verwalten und buchen duerfen.
        planning.MapPut("/{id:guid}/vouchers/{voucherId:guid}", AssignVoucherAsync)
            .RequirePermission(Permission.FinanceBook);
        planning.MapDelete("/vouchers/{voucherId:guid}", UnassignVoucherAsync)
            .RequirePermission(Permission.FinanceBook);

        return app;
    }

    private static IResult ListStatuses() =>
        TypedResults.Ok(ProductionStatuses.All.Select(status => new ProductionStatusInfo(
            status, status.ToString(), status.Label(), status.IsCurrent())));

    private static async Task<IResult> ListAsync(
        ProductionService productions,
        ClaimsPrincipal user,
        ICurrentUser currentUser,
        [FromQuery] bool includeArchived = true,
        CancellationToken token = default)
    {
        List<Production> found = await productions.ListAsync(includeArchived, token);
        Dictionary<Guid, ProductionFigures> figures = await productions.FiguresAsync(
            [.. found.Select(p => p.Id)], Viewer(user, currentUser), token);

        return TypedResults.Ok(found.Select(p => ProductionResponse.From(p, figures[p.Id], Darf(user, Permission.FinanceRead))));
    }

    private static async Task<IResult> CastingsAsync(
        Guid contactId, ProductionService productions, CancellationToken token) =>
        TypedResults.Ok((await productions.CastingsAsync(contactId, token)).Select(CastingResponse.From));

    private static async Task<IResult> GetAsync(
        Guid id,
        ProductionService productions,
        ClaimsPrincipal user,
        ICurrentUser currentUser,
        CancellationToken token)
    {
        ProductionOverview? overview = await productions.OverviewAsync(id, Viewer(user, currentUser), token);
        return overview is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(OverviewResponse.From(overview, Darf(user, Permission.FinanceRead)));
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] ProductionRequest request, ProductionService productions, CancellationToken token)
    {
        var production = new Production { Title = request.Title.Trim() };
        Apply(request, production);

        try
        {
            await productions.AddAsync(production, Plan(request), token);
        }
        catch (ProductionException exception)
        {
            return Rejected(exception);
        }

        Production created = await productions.FindAsync(production.Id, token) ?? production;
        return TypedResults.Created($"/api/productions/{production.Id}", ProductionResponse.From(created, null, false));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, [FromBody] ProductionRequest request, ProductionService productions, CancellationToken token)
    {
        try
        {
            Production updated = await productions.UpdateAsync(id, production =>
            {
                production.Title = request.Title.Trim();
                Apply(request, production);
            }, Plan(request), token);

            Production reloaded = await productions.FindAsync(updated.Id, token) ?? updated;
            return TypedResults.Ok(ProductionResponse.From(reloaded, null, false));
        }
        catch (ProductionException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> DeleteAsync(Guid id, ProductionService productions, CancellationToken token)
    {
        try
        {
            await productions.DeleteAsync(id, token);
        }
        catch (ProductionException exception)
        {
            return Rejected(exception);
        }

        return TypedResults.NoContent();
    }

    private static Task<IResult> RolesAsync(
        Guid id, [FromBody] List<ProductionRoleRequest> request, ProductionService productions, CancellationToken token) =>
        PlanAsync(id, new ProductionPlan(Roles: Roles(request)), productions, token);

    private static Task<IResult> BudgetAsync(
        Guid id, [FromBody] List<ProductionBudgetLineRequest> request, ProductionService productions, CancellationToken token) =>
        PlanAsync(id, new ProductionPlan(BudgetLines: BudgetLines(request)), productions, token);

    private static Task<IResult> NeedsAsync(
        Guid id, [FromBody] List<ProductionNeedRequest> request, ProductionService productions, CancellationToken token) =>
        PlanAsync(id, new ProductionPlan(Needs: Needs(request)), productions, token);

    private static async Task<IResult> PlanAsync(Guid id, ProductionPlan plan, ProductionService productions, CancellationToken token)
    {
        try
        {
            Production updated = await productions.UpdateAsync(id, _ => { }, plan, token);
            Production reloaded = await productions.FindAsync(updated.Id, token) ?? updated;
            return TypedResults.Ok(ProductionResponse.From(reloaded, null, false));
        }
        catch (ProductionException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> AddRoleAsync(
        Guid id, [FromBody] ProductionRoleRequest request, ProductionService productions, CancellationToken token)
    {
        try
        {
            ProductionRole role = await productions.AddRoleAsync(
                id, new ProductionRoleInput(request.Name ?? string.Empty, request.FunctionId, request.ContactId, Clean(request.Note)), token);
            Production reloaded = await productions.FindAsync(id, token) ?? throw new ProductionException("Diese Produktion gibt es nicht.");
            ProductionRole shown = reloaded.Roles.First(r => r.Id == role.Id);
            return TypedResults.Created($"/api/productions/{id}/roles/{role.Id}", ProductionRoleResponse.From(shown));
        }
        catch (ProductionException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> RemoveRoleAsync(
        Guid id, Guid roleId, ProductionService productions, CancellationToken token)
    {
        try
        {
            await productions.RemoveRoleAsync(id, roleId, token);
        }
        catch (ProductionException exception)
        {
            return Rejected(exception);
        }

        return TypedResults.NoContent();
    }

    private static async Task<IResult> AssignVoucherAsync(
        Guid id, Guid voucherId, ProductionService productions, CancellationToken token)
    {
        try
        {
            await productions.AssignVoucherAsync(id, voucherId, token);
        }
        catch (ProductionException exception)
        {
            return Rejected(exception);
        }

        return TypedResults.NoContent();
    }

    private static async Task<IResult> UnassignVoucherAsync(
        Guid voucherId, ProductionService productions, CancellationToken token)
    {
        try
        {
            await productions.AssignVoucherAsync(null, voucherId, token);
        }
        catch (ProductionException exception)
        {
            return Rejected(exception);
        }

        return TypedResults.NoContent();
    }

    private static void Apply(ProductionRequest request, Production production)
    {
        production.Subtitle = Clean(request.Subtitle);
        production.Status = request.Status ?? production.Status;
        production.PremiereDate = request.PremiereDate;
        production.ClosingDate = request.ClosingDate;
        production.Budget = request.Budget;
        production.Description = Clean(request.Description);
        production.Research = Clean(request.Research);
        production.Notes = Clean(request.Notes);
    }

    private static ProductionPlan? Plan(ProductionRequest request) =>
        request.Roles is null && request.BudgetLines is null && request.Needs is null
            ? null
            : new ProductionPlan(Roles(request.Roles), BudgetLines(request.BudgetLines), Needs(request.Needs));

    private static List<ProductionRoleInput>? Roles(List<ProductionRoleRequest>? rows) =>
        rows?.Select(r => new ProductionRoleInput(r.Name ?? string.Empty, r.FunctionId, r.ContactId, Clean(r.Note))).ToList();

    private static List<ProductionBudgetLineInput>? BudgetLines(List<ProductionBudgetLineRequest>? rows) =>
        rows?.Select(l => new ProductionBudgetLineInput(l.Title ?? string.Empty, l.Amount, Clean(l.Category), Clean(l.Note))).ToList();

    private static List<ProductionNeedInput>? Needs(List<ProductionNeedRequest>? rows) =>
        rows?.Select(n => new ProductionNeedInput(n.Title ?? string.Empty, n.Kind ?? ProductionNeedKind.Material, Clean(n.Quantity), n.IsDone, Clean(n.Note))).ToList();

    private static string? Clean(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static DocumentViewer Viewer(ClaimsPrincipal user, ICurrentUser currentUser) =>
        new(currentUser.Id, Darf(user, Permission.DocumentsManage));

    private static bool Darf(ClaimsPrincipal user, Permission permission) =>
        user.HasClaim(PermissionClaimsFactory.ClaimType, ((int)permission).ToString());

    private static IResult Rejected(ProductionException exception) =>
        TypedResults.ValidationProblem(
            new Dictionary<string, string[]> { ["Produktion"] = [.. exception.Problems] },
            title: "Das wurde nicht angenommen.");
}

public record ProductionStatusInfo(ProductionStatus Status, string Key, string Label, bool IsCurrent);

public record ProductionRoleRequest(string? Name, Guid? FunctionId, Guid? ContactId, string? Note);

public record ProductionBudgetLineRequest(string? Title, decimal Amount, string? Category, string? Note);

public record ProductionNeedRequest(string? Title, ProductionNeedKind? Kind, string? Quantity, bool IsDone, string? Note);

/// <summary>
/// Der Bogen der Produktion. Die drei Listen sind freiwillig: Fehlt eine,
/// bleibt ihr Stand -- so kann der Bogen die Stammdaten aendern, ohne die
/// Besetzung mitzuschicken.
/// </summary>
public record ProductionRequest(
    string Title,
    string? Subtitle,
    ProductionStatus? Status,
    DateOnly? PremiereDate,
    DateOnly? ClosingDate,
    decimal? Budget,
    string? Description,
    string? Research,
    string? Notes,
    List<ProductionRoleRequest>? Roles = null,
    List<ProductionBudgetLineRequest>? BudgetLines = null,
    List<ProductionNeedRequest>? Needs = null);

public record ProductionRoleResponse(
    Guid Id,
    string Name,
    Guid? FunctionId,
    string? FunctionLabel,
    Guid? ContactId,
    string? ContactName,
    bool IsCast,
    string? Note)
{
    public static ProductionRoleResponse From(ProductionRole r) => new(
        r.Id, r.Name, r.FunctionId, r.Function?.Label, r.ContactId, r.Contact?.Name, r.IsCast, r.Note);
}

public record ProductionBudgetLineResponse(Guid Id, string Title, string? Category, decimal Amount, string? Note)
{
    public static ProductionBudgetLineResponse From(ProductionBudgetLine l) => new(l.Id, l.Title, l.Category, l.Amount, l.Note);
}

public record ProductionNeedResponse(Guid Id, ProductionNeedKind Kind, string Title, string? Quantity, bool IsDone, string? Note)
{
    public static ProductionNeedResponse From(ProductionNeed n) => new(n.Id, n.Kind, n.Title, n.Quantity, n.IsDone, n.Note);
}

/// <summary>Eine Rolle aus Sicht des Kontakts, mit der Produktion dazu.</summary>
public record CastingResponse(
    Guid ProductionId, string ProductionTitle, ProductionStatus Status, string StatusLabel, bool IsCurrent,
    Guid RoleId, string RoleName, string? FunctionLabel)
{
    public static CastingResponse From(Casting c) => new(
        c.Production.Id, c.Production.Title, c.Production.Status, c.Production.Status.Label(), c.Production.Status.IsCurrent(),
        c.Role.Id, c.Role.Name, c.Role.Function?.Label);
}

/// <summary>
/// Die Zahlen an der Produktion. Kosten und Einnahmen fehlen, wer die
/// Finanzen nicht sehen darf -- die Zahl verriete sonst, was der Beleg nicht
/// zeigt. Der Plan (Summe der Positionen) ist Vorgabe, kein Beleg, und
/// bleibt sichtbar.
/// </summary>
public record FiguresResponse(
    int Roles,
    int OpenRoles,
    int OpenTasks,
    int Tasks,
    int Events,
    DateTimeOffset? NextEvent,
    int Documents,
    decimal Planned,
    decimal? Costs,
    decimal? Income,
    int Tours,
    int Needs,
    int OpenNeeds,
    int Papers,
    int OpenPapers,
    IReadOnlyList<ActivityWeekResponse> Activity)
{
    public static FiguresResponse From(ProductionFigures f, bool withFinance) => new(
        f.Roles, f.OpenRoles, f.OpenTasks, f.Tasks, f.Events, f.NextEvent, f.Documents, f.Planned,
        withFinance ? f.Costs : null, withFinance ? f.Income : null, f.Tours,
        f.Needs, f.OpenNeeds, f.Papers, f.OpenPapers,
        [.. f.Activity.Select(ActivityWeekResponse.From)]);
}

/// <summary>Eine Woche Herzschlag -- Zaehlungen, keine Betraege, deshalb fuer alle sichtbar.</summary>
public record ActivityWeekResponse(DateOnly Week, int Tasks, int Events, int Vouchers, int Documents, int Total)
{
    public static ActivityWeekResponse From(ActivityWeek w) => new(w.Week, w.Tasks, w.Events, w.Vouchers, w.Documents, w.Total);
}

public record ProductionResponse(
    Guid Id,
    string Title,
    string? Subtitle,
    ProductionStatus Status,
    string StatusLabel,
    bool IsCurrent,
    DateOnly? PremiereDate,
    DateOnly? ClosingDate,
    decimal? Budget,
    string? Description,
    string? Research,
    string? Notes,
    IReadOnlyList<ProductionRoleResponse> Roles,
    IReadOnlyList<ProductionBudgetLineResponse> BudgetLines,
    IReadOnlyList<ProductionNeedResponse> Needs,
    FiguresResponse? Figures)
{
    public static ProductionResponse From(Production p, ProductionFigures? figures, bool withFinance) => new(
        p.Id, p.Title, p.Subtitle, p.Status, p.Status.Label(), p.Status.IsCurrent(),
        p.PremiereDate, p.ClosingDate, p.Budget, p.Description, p.Research, p.Notes,
        [.. p.Roles.Select(ProductionRoleResponse.From)],
        [.. p.BudgetLines.Select(ProductionBudgetLineResponse.From)],
        [.. p.Needs.Select(ProductionNeedResponse.From)],
        figures is null ? null : FiguresResponse.From(figures, withFinance));
}

public record ProductionTaskResponse(
    Guid Id, string Title, TaskState State, string StateLabel, bool IsOpen, bool IsOverdue,
    DateOnly? DueDate, string? AssigneeName, string? GroupName, string? CategoryLabel, string? CategoryColor)
{
    public static ProductionTaskResponse From(TaskItem t, DateOnly today) => new(
        t.Id, t.Title, t.State, t.State.Label(), t.IsOpen, t.IsOverdue(today), t.DueDate,
        t.Assignee?.Name, t.Group?.Name, t.Category?.Label, t.Category?.Color);
}

public record ProductionEventResponse(
    Guid Id, string Title, string? Location, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    bool IsAllDay, EventStatus Status, string StatusLabel, bool IsException)
{
    public static ProductionEventResponse From(CalendarEvent e) => new(
        e.Id, e.Title, e.Location, e.StartsAt, e.EndsAt, e.IsAllDay, e.Status, e.Status.Label(), e.IsException);
}

public record ProductionSeriesResponse(
    Guid Id, string Title, string? Location, DateOnly StartDate, DateOnly? Until, string Rule, EventStatus Status)
{
    public static ProductionSeriesResponse From(EventSeries s) => new(
        s.Id, s.Title, s.Location, s.StartDate, s.Until, s.Describe(), s.Status);
}

public record ProductionVoucherResponse(
    Guid Id, string Label, int FiscalYear, DateOnly Date, decimal GrossAmount, string Description,
    string? ContactName, VoucherDirection Direction, bool IsBooked, bool IsCommitted)
{
    public static ProductionVoucherResponse From(Voucher v) => new(
        v.Id, v.Label, v.FiscalYear, v.Date, v.GrossAmount, v.Description, v.Contact?.Name,
        v.Direction, v.IsBooked, v.CommittedAt is not null);
}

public record ProductionDocumentResponse(Guid Id, string Name, string ContentType, long SizeInBytes, IReadOnlyList<string> Tags)
{
    public static ProductionDocumentResponse From(Document d) => new(
        d.Id, d.Name, d.ContentType, d.SizeInBytes, [.. d.Tags.Select(t => t.Tag).Order()]);
}

public record ProductionTourResponse(
    Guid Id, string Title, DateOnly? StartsOn, DateOnly? EndsOn, int Stops, int BindingStops, int Performances, decimal ConfirmedFees)
{
    public static ProductionTourResponse From(Tour t) => new(
        t.Id, t.Title, t.Span?.From, t.Span?.To, t.Stops.Count,
        t.Stops.Count(s => s.Status.IsBinding()),
        t.Stops.Where(s => s.Status.AppearsInCalendar()).Sum(s => s.Performances.Count),
        t.Stops.Where(s => s.Status.IsBinding()).Sum(s => s.Fee ?? 0m));
}

public record OverviewResponse(
    ProductionResponse Production,
    IReadOnlyList<ProductionTaskResponse> Tasks,
    IReadOnlyList<ProductionEventResponse> Events,
    IReadOnlyList<ProductionSeriesResponse> Series,
    IReadOnlyList<ProductionVoucherResponse>? Vouchers,
    IReadOnlyList<ProductionDocumentResponse> Documents,
    IReadOnlyList<ProductionTourResponse> Tours)
{
    public static OverviewResponse From(ProductionOverview o, bool withFinance)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        return new(
            ProductionResponse.From(o.Production, o.Figures, withFinance),
            [.. o.Tasks.Select(t => ProductionTaskResponse.From(t, today))],
            [.. o.Events.Select(ProductionEventResponse.From)],
            [.. o.Series.Select(ProductionSeriesResponse.From)],
            withFinance ? [.. o.Vouchers.Select(ProductionVoucherResponse.From)] : null,
            [.. o.Documents.Select(ProductionDocumentResponse.From)],
            [.. o.Tours.Select(ProductionTourResponse.From)]);
    }
}
