using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Souffleur.Domain.Identity;
using Souffleur.Domain.Resources;
using Souffleur.Infrastructure.Resources;
using Souffleur.Infrastructure.Identity;
using Souffleur.Api.Infrastructure;

namespace Souffleur.Api.Features.Resources;

/// <summary>
/// Die Ressourcen des Hauses: Bühnen, Probebühnen, Werkstätten, Fahrzeuge.
/// </summary>
/// <remarks>
/// Nicht hinter dem Kalendermodul: Eine Werkstatt belegt auch, wer Aufgaben
/// plant. Lesen darf jeder Angemeldete -- ohne die Liste ließe sich kein
/// Termin anlegen. Anlegen und Abschalten darf, wer das Haus einrichtet.
/// </remarks>
public static class ResourceEndpoints
{
    public static IEndpointRouteBuilder MapResourceEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/resources").WithTags("Ressourcen");

        group.MapGet("/", ListAsync).RequireAuthorization();
        group.MapGet("/{id:guid}", GetAsync).RequireAuthorization();
        group.MapPost("/", CreateAsync).RequireAuthorization(Manage);
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(Manage);
        group.MapPost("/{id:guid}/active", SetActiveAsync).RequireAuthorization(Manage);
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(Manage);
        group.MapPost("/order", ReorderAsync).RequireAuthorization(Manage);

        return app;
    }

    private static void Manage(AuthorizationPolicyBuilder policy) =>
        policy.RequireClaim(PermissionClaimsFactory.ClaimType, ((int)Permission.ResourcesManage).ToString());

    private static async Task<IResult> ListAsync(
        ResourceService resources,
        [FromQuery] bool includeInactive = false,
        CancellationToken token = default)
    {
        List<Resource> found = await resources.ListAsync(includeInactive, token);

        List<ResourceResponse> result = [];
        foreach (Resource resource in found)
        {
            result.Add(ResourceResponse.From(resource, await resources.UsageCountAsync(resource.Id, token)));
        }

        return TypedResults.Ok(result);
    }

    private static async Task<IResult> GetAsync(
        Guid id, ResourceService resources, CancellationToken token) =>
        await resources.FindAsync(id, token) is { } found
            ? TypedResults.Ok(ResourceResponse.From(found, await resources.UsageCountAsync(id, token)))
            : TypedResults.NotFound();

    private static async Task<IResult> CreateAsync(
        [FromBody] ResourceRequest request, ResourceService resources, CancellationToken token)
    {
        var resource = new Resource { Name = request.Name.Trim() };
        Apply(request, resource);

        try
        {
            await resources.AddAsync(resource, token);
        }
        catch (ResourceException exception)
        {
            return Rejected(exception);
        }

        // Noch einmal geladen, damit die Art mit Beschriftung und Farbe
        // dransteht -- beim Anlegen ist nur ihre Id bekannt.
        Resource created = await resources.FindAsync(resource.Id, token) ?? resource;
        return TypedResults.Created(
            $"/api/resources/{resource.Id}", ResourceResponse.From(created, 0));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] ResourceRequest request,
        ResourceService resources,
        CancellationToken token)
    {
        try
        {
            Resource updated = await resources.UpdateAsync(id, resource =>
            {
                resource.Name = request.Name.Trim();
                Apply(request, resource);
            }, token);

            Resource reloaded = await resources.FindAsync(updated.Id, token) ?? updated;
            return TypedResults.Ok(
                ResourceResponse.From(reloaded, await resources.UsageCountAsync(id, token)));
        }
        catch (ResourceException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> SetActiveAsync(
        Guid id,
        [FromBody] ResourceActiveRequest request,
        ResourceService resources,
        CancellationToken token)
    {
        try
        {
            await resources.SetActiveAsync(id, request.IsActive, token);
        }
        catch (ResourceException exception)
        {
            return Rejected(exception);
        }

        return TypedResults.NoContent();
    }

    private static async Task<IResult> DeleteAsync(
        Guid id, ResourceService resources, CancellationToken token)
    {
        try
        {
            await resources.DeleteAsync(id, token);
        }
        catch (ResourceException exception)
        {
            return Rejected(exception);
        }

        return TypedResults.NoContent();
    }

    private static async Task<IResult> ReorderAsync(
        [FromBody] IReadOnlyList<Guid> order, ResourceService resources, CancellationToken token)
    {
        await resources.ReorderAsync(order, token);
        return TypedResults.NoContent();
    }

    private static void Apply(ResourceRequest request, Resource resource)
    {
        resource.ShortName = Trimmed(request.ShortName);
        resource.Description = Trimmed(request.Description);
        resource.KindId = request.KindId;
        resource.Capacity = request.Capacity;
        resource.IsExclusive = request.IsExclusive;

        if (request.SortOrder is { } order)
        {
            resource.SortOrder = order;
        }

        if (request.IsActive is { } active)
        {
            resource.IsActive = active;
        }
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IResult Rejected(ResourceException exception) =>
        TypedResults.ValidationProblem(
            new Dictionary<string, string[]> { ["Ressource"] = [.. exception.Problems] },
            title: "Die Ressource wurde nicht angenommen.");
}

public record ResourceRequest(
    string Name,
    string? ShortName = null,
    Guid? KindId = null,
    string? Description = null,
    int? Capacity = null,
    bool IsExclusive = true,
    int? SortOrder = null,
    bool? IsActive = null);

public record ResourceActiveRequest(bool IsActive);

public record ResourceResponse(
    Guid Id,
    string Name,
    string? ShortName,
    string Display,
    Guid? KindId,
    string? KindLabel,
    string? KindColor,
    string? Description,
    int? Capacity,
    bool IsExclusive,
    int SortOrder,
    bool IsActive,
    int UsageCount,
    bool CanBeDeleted)
{
    public static ResourceResponse From(Resource resource, int usage) => new(
        resource.Id,
        resource.Name,
        resource.ShortName,
        resource.Display,
        resource.KindId,
        resource.Kind?.Label,
        resource.Kind?.Color,
        resource.Description,
        resource.Capacity,
        resource.IsExclusive,
        resource.SortOrder,
        resource.IsActive,
        usage,
        usage == 0);
}
