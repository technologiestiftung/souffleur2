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
/// Die Kuenstlersozialabgabe.
/// </summary>
/// <remarks>
/// Nur fuer Leitung, Verwaltung und Buchhaltung: Hier stehen Honorarsummen je
/// Person, und das sind Personaldaten und Finanzzahlen zugleich.
/// </remarks>
public static class ArtistsSocialFundEndpoints
{
    public static IEndpointRouteBuilder MapArtistsSocialFundEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/personnel/ksk")
            .WithTags("Künstlersozialabgabe")
            .RequireModule(SouffleurModule.Personnel)
            .RequireAuthorization(Read);

        group.MapGet("/", ReportAsync);
        group.MapGet("/rates", RatesAsync);
        group.MapPut("/rates/{year:int}", SetRateAsync);

        return app;
    }

    private static void Read(AuthorizationPolicyBuilder policy) =>
        policy.RequireClaim(PermissionClaimsFactory.ClaimType, ((int)Permission.PersonnelLevy).ToString());

    private static async Task<IResult> ReportAsync(
        ArtistsSocialFundService fund,
        [FromQuery] int? year = null,
        CancellationToken token = default) =>
        TypedResults.Ok(await fund.ReportAsync(year ?? DateTime.UtcNow.Year, token));

    private static async Task<IResult> RatesAsync(
        ArtistsSocialFundService fund,
        CancellationToken token) =>
        TypedResults.Ok(
            (await fund.RatesAsync(token)).Select(r => new RateResponse(r.Year, r.Percent)));

    private static async Task<IResult> SetRateAsync(
        int year,
        [FromBody] RateRequest request,
        ArtistsSocialFundService fund,
        CancellationToken token)
    {
        try
        {
            ArtistsSocialFundRate rate = await fund.SetRateAsync(year, request.Percent, token);
            return TypedResults.Ok(new RateResponse(rate.Year, rate.Percent));
        }
        catch (ArtistsSocialFundException exception)
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["Abgabesatz"] = [.. exception.Problems] },
                title: "Der Abgabesatz wurde nicht angenommen.");
        }
    }
}

public record RateRequest(decimal Percent);

public record RateResponse(int Year, decimal Percent);
