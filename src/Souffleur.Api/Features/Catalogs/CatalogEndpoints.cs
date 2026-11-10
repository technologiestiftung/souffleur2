using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Souffleur.Domain.Catalogs;
using Souffleur.Domain.Identity;
using Souffleur.Infrastructure.Catalogs;
using Souffleur.Infrastructure.Identity;
using Souffleur.Api.Infrastructure;

namespace Souffleur.Api.Features.Catalogs;

/// <summary>
/// Die Listen des Hauses -- Terminkategorien, Aufgabenarten, Kostenarten.
/// Siehe ADR-0005.
/// </summary>
/// <remarks>
/// Eine Gruppe von Endpunkten für alle Listen, nicht eine je Liste: Dieselben
/// Regeln, dieselbe Verwaltung, dieselbe Oberfläche. Eine neue Liste braucht
/// hier nichts.
/// </remarks>
public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/catalogs").WithTags("Listen des Hauses");

        // Lesen darf jeder Angemeldete: Ohne die Kategorien lässt sich kein
        // Termin anlegen. Ändern darf nur, wer das Haus einrichtet.
        group.MapGet("/", ListCatalogs).RequireAuthorization();
        group.MapGet("/{catalog}", ListAsync).RequireAuthorization();
        group.MapPost("/{catalog}", CreateAsync).RequireAuthorization(Manage);
        group.MapPut("/{catalog}/{id:guid}", UpdateAsync).RequireAuthorization(Manage);
        group.MapPost("/{catalog}/{id:guid}/active", SetActiveAsync).RequireAuthorization(Manage);
        group.MapDelete("/{catalog}/{id:guid}", DeleteAsync).RequireAuthorization(Manage);
        group.MapPost("/{catalog}/order", ReorderAsync).RequireAuthorization(Manage);

        return app;
    }

    private static void Manage(AuthorizationPolicyBuilder policy) =>
        policy.RequireClaim(PermissionClaimsFactory.ClaimType, ((int)Permission.CatalogsManage).ToString());

    private static IResult ListCatalogs() =>
        TypedResults.Ok(CatalogKinds.All.Select(kind => new CatalogInfo(
            kind,
            kind.ToString(),
            kind.Label(),
            kind.Description(),
            kind.AllowsHierarchy(),
            kind.UsesColor())));

    private static async Task<IResult> ListAsync(
        CatalogKind catalog,
        CatalogService catalogs,
        [FromQuery] bool includeInactive = false,
        CancellationToken token = default)
    {
        List<CatalogEntry> entries = await catalogs.ListAsync(catalog, includeInactive, token);

        // Die Verwendungszahl steht dran, damit die Oberfläche „löschen" gar
        // nicht erst anbietet, wo nur Abschalten möglich ist.
        List<CatalogEntryResponse> result = [];
        foreach (CatalogEntry entry in entries)
        {
            result.Add(CatalogEntryResponse.From(entry, await catalogs.UsageCountAsync(entry, token)));
        }

        return TypedResults.Ok(result);
    }

    private static async Task<IResult> CreateAsync(
        CatalogKind catalog,
        [FromBody] CatalogEntryRequest request,
        CatalogService catalogs,
        CancellationToken token)
    {
        var entry = new CatalogEntry
        {
            Catalog = catalog,
            Key = request.Key ?? string.Empty,
            Label = request.Label,
        };
        Apply(request, entry);

        try
        {
            await catalogs.AddAsync(entry, token);
        }
        catch (CatalogException exception)
        {
            return Rejected(exception);
        }

        return TypedResults.Created(
            $"/api/catalogs/{catalog}/{entry.Id}", CatalogEntryResponse.From(entry, 0));
    }

    private static async Task<IResult> UpdateAsync(
        CatalogKind catalog,
        Guid id,
        [FromBody] CatalogEntryRequest request,
        CatalogService catalogs,
        CancellationToken token)
    {
        try
        {
            CatalogEntry entry = await catalogs.UpdateAsync(id, e =>
            {
                e.Label = request.Label;
                Apply(request, e);
            }, token);

            return TypedResults.Ok(
                CatalogEntryResponse.From(entry, await catalogs.UsageCountAsync(entry, token)));
        }
        catch (CatalogException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> SetActiveAsync(
        CatalogKind catalog,
        Guid id,
        [FromBody] ActiveRequest request,
        CatalogService catalogs,
        CancellationToken token)
    {
        try
        {
            await catalogs.SetActiveAsync(id, request.IsActive, token);
        }
        catch (CatalogException exception)
        {
            return Rejected(exception);
        }

        return TypedResults.NoContent();
    }

    private static async Task<IResult> DeleteAsync(
        CatalogKind catalog, Guid id, CatalogService catalogs, CancellationToken token)
    {
        try
        {
            await catalogs.DeleteAsync(id, token);
        }
        catch (CatalogException exception)
        {
            return Rejected(exception);
        }

        return TypedResults.NoContent();
    }

    private static async Task<IResult> ReorderAsync(
        CatalogKind catalog,
        [FromBody] IReadOnlyList<Guid> order,
        CatalogService catalogs,
        CancellationToken token)
    {
        await catalogs.ReorderAsync(catalog, order, token);
        return TypedResults.NoContent();
    }

    private static void Apply(CatalogEntryRequest request, CatalogEntry entry)
    {
        entry.Code = Trimmed(request.Code);
        entry.Description = Trimmed(request.Description);
        entry.Color = Trimmed(request.Color);
        entry.ParentId = request.ParentId;

        if (request.SortOrder is { } order)
        {
            entry.SortOrder = order;
        }

        if (request.IsActive is { } active)
        {
            entry.IsActive = active;
        }
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IResult Rejected(CatalogException exception) =>
        TypedResults.ValidationProblem(
            new Dictionary<string, string[]> { ["Eintrag"] = [.. exception.Problems] },
            title: "Der Eintrag wurde nicht angenommen.");
}

public record CatalogInfo(
    CatalogKind Catalog,
    string Key,
    string Label,
    string Description,
    bool AllowsHierarchy,
    bool UsesColor);

public record CatalogEntryRequest(
    string Label,
    string? Key = null,
    string? Code = null,
    string? Description = null,
    string? Color = null,
    int? SortOrder = null,
    bool? IsActive = null,
    Guid? ParentId = null);

public record ActiveRequest(bool IsActive);

public record CatalogEntryResponse(
    Guid Id,
    CatalogKind Catalog,
    string Key,
    string Label,
    string? Code,
    string? Description,
    string? Color,
    int SortOrder,
    bool IsActive,
    bool IsSystem,
    Guid? ParentId,
    int UsageCount,
    bool CanBeDeleted)
{
    public static CatalogEntryResponse From(CatalogEntry entry, int usage) => new(
        entry.Id, entry.Catalog, entry.Key, entry.Label, entry.Code, entry.Description,
        entry.Color, entry.SortOrder, entry.IsActive, entry.IsSystem, entry.ParentId,
        usage, !entry.IsSystem && usage == 0);
}
