using Microsoft.AspNetCore.Mvc;
using Souffleur.Domain.Identity;
using Souffleur.Domain.Modules;
using Souffleur.Infrastructure.Modules;
using Souffleur.Infrastructure.Identity;
using Souffleur.Api.Infrastructure;

namespace Souffleur.Api.Features.Modules;

/// <summary>
/// Welche Module dieses Haus nutzt. Lesen darf jeder Angemeldete -- die
/// Oberflaeche baut daraus ihre Reiter. Aendern darf nur die Verwaltung.
/// </summary>
public static class ModuleEndpoints
{
    public static IEndpointRouteBuilder MapModuleEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/modules").WithTags("Module");

        group.MapGet("/", ListAsync).RequireAuthorization();
        group.MapPut("/{module}", SetAsync)
             .RequirePermission(Permission.ModulesManage);

        return app;
    }

    private static async Task<IResult> ListAsync(ModuleService modules)
    {
        IReadOnlyList<ModuleSetting> settings = await modules.ListAsync();

        return TypedResults.Ok(settings.Select(s => new ModuleResponse(
            s.Module,
            s.Module.ToString(),
            s.Module.Label(),
            s.IsEnabled,
            s.Module.CanBeDisabled(),
            s.Note)));
    }

    private static async Task<IResult> SetAsync(
        SouffleurModule module,
        [FromBody] ModuleRequest request,
        ModuleService modules)
    {
        if (!Enum.IsDefined(module))
        {
            return TypedResults.NotFound();
        }

        try
        {
            await modules.SetAsync(module, request.IsEnabled, request.Note);
        }
        catch (InvalidOperationException exception)
        {
            return TypedResults.Problem(exception.Message, statusCode: StatusCodes.Status409Conflict);
        }

        return TypedResults.NoContent();
    }
}

public record ModuleRequest(bool IsEnabled, string? Note = null);

public record ModuleResponse(
    SouffleurModule Module,
    string Key,
    string Label,
    bool IsEnabled,
    bool CanBeDisabled,
    string? Note);
