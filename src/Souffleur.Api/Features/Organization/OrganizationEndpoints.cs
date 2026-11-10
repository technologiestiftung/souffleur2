using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Accounting;
using Souffleur.Domain.Identity;
using Souffleur.Domain.Organizations;
using Souffleur.Infrastructure.Persistence;
using Souffleur.Infrastructure.Identity;
using Souffleur.Api.Infrastructure;

namespace Souffleur.Api.Features.Organization;

/// <summary>
/// Die Stammdaten des Hauses und seine Buchhaltungsregeln.
/// </summary>
/// <remarks>
/// Hier entscheidet das Haus, nach welchem Kontenrahmen es bucht. souffleur
/// liefert keinen mit -- was gilt, sagt das Steuerbuero.
/// </remarks>
public static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/organization").WithTags("Haus");

        group.MapGet("/", GetAsync).RequireAuthorization();
        group.MapPut("/", SaveAsync)
             .RequirePermission(Permission.HouseManage);
        group.MapGet("/charts-of-accounts", ListCharts).RequireAuthorization();
        group.MapGet("/tax-spheres", ListSpheres).RequireAuthorization();
        group.MapGet("/federal-states", ListStates).RequireAuthorization();
        group.MapPut("/federal-state", SaveStateAsync)
             .RequirePermission(Permission.HouseManage);

        return app;
    }

    private static async Task<IResult> GetAsync(SouffleurDbContext context)
    {
        Domain.Organizations.Organization? organization =
            await context.Organizations.AsNoTracking().FirstOrDefaultAsync();

        return organization is null
            ? TypedResults.NoContent()
            : TypedResults.Ok(OrganizationResponse.From(organization));
    }

    private static async Task<IResult> SaveAsync(
        [FromBody] OrganizationRequest request,
        SouffleurDbContext context)
    {
        if (!Enum.IsDefined(request.ChartOfAccounts))
        {
            return TypedResults.Problem(
                "Diesen Kontenrahmen gibt es nicht.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Name"] = ["Das Haus braucht einen Namen."],
            });
        }

        Domain.Organizations.Organization? organization =
            await context.Organizations.FirstOrDefaultAsync();

        if (organization is null)
        {
            organization = new Domain.Organizations.Organization { Name = request.Name };
            context.Organizations.Add(organization);
        }

        organization.Name = request.Name.Trim();
        organization.LegalForm = request.LegalForm;
        organization.Street = request.Street;
        organization.PostalCode = request.PostalCode;
        organization.City = request.City;
        organization.TaxNumber = request.TaxNumber;
        organization.VatId = request.VatId;
        organization.KskNumber = request.KskNumber;
        organization.ChartOfAccounts = request.ChartOfAccounts;
        organization.IsNonProfit = request.IsNonProfit;

        // Wer die Stammdaten ohne Bundesland schickt -- eine aeltere
        // Oberflaeche --, soll es nicht auf Berlin zuruecksetzen.
        if (request.FederalState is { } state && Enum.IsDefined(state))
        {
            organization.FederalState = state;
        }

        await context.SaveChangesAsync();

        return TypedResults.Ok(OrganizationResponse.From(organization));
    }

    private static IResult ListStates() =>
        TypedResults.Ok(FederalStates.All.Select(state => new StateResponse((int)state, state.ToString(), state.Label())));

    /// <summary>
    /// Nur das Bundesland. Eigener Weg, weil es die Feiertage bestimmt und auf
    /// der Seite der freien Tage gesetzt wird, nicht mit Steuernummer und
    /// Kontenrahmen zusammen.
    /// </summary>
    private static async Task<IResult> SaveStateAsync(
        [FromBody] StateRequest request,
        SouffleurDbContext context)
    {
        if (!Enum.IsDefined(request.FederalState))
        {
            return TypedResults.Problem(
                "Dieses Bundesland gibt es nicht.", statusCode: StatusCodes.Status400BadRequest);
        }

        Domain.Organizations.Organization? organization = await context.Organizations.FirstOrDefaultAsync();
        if (organization is null)
        {
            return TypedResults.NotFound();
        }

        organization.FederalState = request.FederalState;
        await context.SaveChangesAsync();
        return TypedResults.Ok(OrganizationResponse.From(organization));
    }

    /// <summary>Die Kontenrahmen zur Auswahl, mit dem, woran sie sich unterscheiden.</summary>
    private static IResult ListCharts() =>
        TypedResults.Ok(ChartsOfAccounts.All.Select(chart => new ChartResponse(
            chart,
            chart.ToString(),
            chart.Label(),
            chart.UsesTaxSpheres())));

    /// <summary>Die vier Sphaeren mit Rechtsbegriff und einem Beispiel aus dem Theater.</summary>
    private static IResult ListSpheres() =>
        TypedResults.Ok(TaxSpheres.All.Select(sphere => new SphereResponse(
            sphere,
            sphere.ToString(),
            sphere.Label(),
            sphere.Example())));
}

public record OrganizationRequest(
    string Name,
    ChartOfAccounts ChartOfAccounts,
    bool IsNonProfit,
    string? LegalForm = null,
    string? Street = null,
    string? PostalCode = null,
    string? City = null,
    string? TaxNumber = null,
    string? VatId = null,
    string? KskNumber = null,
    FederalState? FederalState = null);

public record StateRequest(FederalState FederalState);

public record StateResponse(int Value, string Key, string Label);

public record OrganizationResponse(
    string Name,
    string? LegalForm,
    string? Street,
    string? PostalCode,
    string? City,
    string? TaxNumber,
    string? VatId,
    string? KskNumber,
    ChartOfAccounts ChartOfAccounts,
    string ChartLabel,
    bool IsNonProfit,
    bool RequiresTaxSpheres,
    bool HasDemoData,
    int FederalState,
    string FederalStateLabel)
{
    public static OrganizationResponse From(Domain.Organizations.Organization o) => new(
        o.Name, o.LegalForm, o.Street, o.PostalCode, o.City, o.TaxNumber, o.VatId, o.KskNumber,
        o.ChartOfAccounts, o.ChartOfAccounts.Label(), o.IsNonProfit, o.RequiresTaxSpheres,
        o.HasDemoData, (int)o.FederalState, o.FederalState.Label());
}

public record ChartResponse(ChartOfAccounts Chart, string Key, string Label, bool UsesTaxSpheres);

public record SphereResponse(TaxSphere Sphere, string Key, string Label, string Example);
