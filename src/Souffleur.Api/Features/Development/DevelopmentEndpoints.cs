using Microsoft.EntityFrameworkCore;
using Souffleur.Api.Infrastructure;
using Souffleur.Domain.Identity;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Api.Features.Development;

/// <summary>
/// Handgriffe, die es nur in der Entwicklung gibt: die Vorfuehrdaten gegen
/// die eines anderen Hauses tauschen, ohne sich neu anzumelden.
/// </summary>
/// <remarks>
/// Wird nur eingehaengt, wenn die Anwendung in der Entwicklung laeuft
/// (Program.cs). Im Betrieb gibt es den Pfad nicht: Ein Haus soll seinen
/// Bestand nicht mit einem Klick verlieren koennen. Zur Sicherheit greift
/// zusaetzlich der Schalter fuer die Entwicklungsdaten -- ohne ihn gibt es
/// nichts zu wechseln, und die Liste bleibt leer.
/// </remarks>
public static class DevelopmentEndpoints
{
    public static IEndpointRouteBuilder MapDevelopmentEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/development").WithTags("Entwicklung");

        group.MapGet("/houses", ListAsync).RequireAuthorization();
        group.MapPost("/houses/{key}", SwitchAsync).RequirePermission(Permission.HouseManage);

        return app;
    }

    private static async Task<IResult> ListAsync(
        SouffleurDbContext context, IConfiguration configuration, HttpContext http, CancellationToken token)
    {
        // Ein Konto, das an ein Haus gebunden ist, erfaehrt nicht, welche es sonst gibt.
        if (!SeedsDevelopmentData(configuration)
            || DemoHouses.IsBoundAccount(configuration, http.User.Identity?.Name))
        {
            return TypedResults.Ok(Array.Empty<DemoHouses.AvailableHouse>());
        }

        string? current = await context.Organizations.AsNoTracking()
            .Select(o => o.Name)
            .FirstOrDefaultAsync(token);

        return TypedResults.Ok(DemoHouses.List(configuration, current));
    }

    private static async Task<IResult> SwitchAsync(
        string key, IServiceProvider services, IConfiguration configuration, HttpContext http, CancellationToken token)
    {
        if (!SeedsDevelopmentData(configuration))
        {
            return TypedResults.NotFound();
        }

        // Nicht ueber die Oberflaeche allein: Wer die Adresse kennt, koennte sonst
        // das Haus eines anderen Ensembles laden.
        if (DemoHouses.IsBoundAccount(configuration, http.User.Identity?.Name))
        {
            return TypedResults.Forbid();
        }

        HouseProfile? profile = await DemoHouses.SwitchAsync(services, key, token);

        return profile is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new SwitchedResponse(key, profile.Name));
    }

    private static bool SeedsDevelopmentData(IConfiguration configuration) =>
        configuration.GetValue("Souffleur:SeedDevelopmentData", false);
}

public record SwitchedResponse(string Key, string Name);
