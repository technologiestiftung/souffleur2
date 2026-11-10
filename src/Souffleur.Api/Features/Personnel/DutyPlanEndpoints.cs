using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Souffleur.Api.Infrastructure;
using Souffleur.Domain.Identity;
using Souffleur.Domain.Modules;
using Souffleur.Domain.Personnel;
using Souffleur.Infrastructure.Personnel;
using Souffleur.Infrastructure.Identity;

namespace Souffleur.Api.Features.Personnel;

/// <summary>
/// Der Dienstplan und der Bedarf dahinter.
/// </summary>
/// <remarks>
/// Lesen darf jeder Angemeldete: Wer wann Dienst hat, steht im Haus ohnehin am
/// schwarzen Brett. Den Bedarf setzt die Dienstplanung.
/// </remarks>
public static class DutyPlanEndpoints
{
    public static IEndpointRouteBuilder MapDutyPlanEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/personnel")
            .WithTags("Dienstplan")
            .RequireModule(SouffleurModule.Personnel)
            .RequireAuthorization();

        group.MapGet("/plan", PlanAsync);
        group.MapGet("/duty-templates", TemplatesAsync);
        group.MapPut("/duty-templates/{categoryId:guid}", SaveAsync).RequireAuthorization(Plan);
        group.MapDelete("/duty-templates/{categoryId:guid}", RemoveAsync).RequireAuthorization(Plan);

        return app;
    }

    private static void Plan(AuthorizationPolicyBuilder policy) =>
        policy.RequireClaim(PermissionClaimsFactory.ClaimType, ((int)Permission.PersonnelPlan).ToString());

    private static async Task<IResult> PlanAsync(
        DutyPlanService duties,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken token) =>
        TypedResults.Ok(await duties.PlanAsync(from, to, token));

    private static async Task<IResult> TemplatesAsync(
        DutyPlanService duties,
        CancellationToken token) =>
        TypedResults.Ok(
            (await duties.TemplatesAsync(token)).Select(TemplateResponse.Of).ToList());

    private static async Task<IResult> SaveAsync(
        Guid categoryId,
        [FromBody] TemplateRequest request,
        DutyPlanService duties,
        CancellationToken token)
    {
        try
        {
            DutyTemplate template = await duties.SaveWithGroupsAsync(
                categoryId,
                [.. request.Requirements.Select(r => (r.FunctionId, r.Count, r.GroupId))],
                request.Note,
                token);

            return TypedResults.Ok(new { template.Id, template.CategoryId });
        }
        catch (DutyPlanException exception)
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["Bedarf"] = [.. exception.Problems] },
                title: "Der Bedarf wurde nicht angenommen.");
        }
    }

    private static async Task<IResult> RemoveAsync(
        Guid categoryId,
        DutyPlanService duties,
        CancellationToken token)
    {
        await duties.RemoveAsync(categoryId, token);
        return TypedResults.NoContent();
    }
}

public record RequirementRequest(Guid FunctionId, int Count, Guid? GroupId = null);

public record TemplateRequest(IReadOnlyList<RequirementRequest> Requirements, string? Note);

public record TemplateResponse(
    Guid CategoryId,
    string CategoryLabel,
    string? Note,
    IReadOnlyList<RequirementResponse> Requirements)
{
    public static TemplateResponse Of(DutyTemplate template) => new(
        template.CategoryId,
        template.Category?.Label ?? "—",
        template.Note,
        [.. template.Requirements.Select(r =>
            new RequirementResponse(r.FunctionId, r.Function?.Label ?? "—", r.Count, r.GroupId, r.Group?.Name))]);
}

public record RequirementResponse(
    Guid FunctionId, string Function, int Count, Guid? GroupId, string? GroupName);
