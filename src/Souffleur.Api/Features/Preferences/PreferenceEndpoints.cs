using Microsoft.AspNetCore.Mvc;
using Souffleur.Infrastructure.Identity;

namespace Souffleur.Api.Features.Preferences;

/// <summary>
/// Was jemand fuer sich eingestellt hat -- die Reihenfolge seiner Reiter etwa.
/// Jeder sieht und aendert nur das Eigene; eine Rolle braucht es dafuer nicht.
/// </summary>
public static class PreferenceEndpoints
{
    /// <summary>
    /// Entspricht der Spaltenbreite in der Datenbank. Wird es laenger, ist es
    /// keine Einstellung mehr, sondern eine Ablage.
    /// </summary>
    private const int HoechstlaengeWert = 4000;

    private const int HoechstlaengeSchluessel = 100;

    public static IEndpointRouteBuilder MapPreferenceEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/preferences")
                                     .WithTags("Einstellungen")
                                     .RequireAuthorization();

        group.MapGet("/", MineAsync);
        group.MapPut("/{key}", SetAsync);
        group.MapDelete("/{key}", RemoveAsync);

        return app;
    }

    private static async Task<IResult> MineAsync(UserPreferenceService preferences) =>
        TypedResults.Ok(await preferences.MineAsync());

    private static async Task<IResult> SetAsync(
        string key,
        [FromBody] PreferenceRequest request,
        UserPreferenceService preferences)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > HoechstlaengeSchluessel)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["key"] = [$"Der Schlüssel fehlt oder ist länger als {HoechstlaengeSchluessel} Zeichen."],
            });
        }

        if (request.Value is null || request.Value.Length > HoechstlaengeWert)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["value"] = [$"Der Wert fehlt oder ist länger als {HoechstlaengeWert} Zeichen."],
            });
        }

        if (!await preferences.SetAsync(key, request.Value))
        {
            return TypedResults.Problem(
                "Für dieses Konto sind keine weiteren Einstellungen möglich.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return TypedResults.NoContent();
    }

    private static async Task<IResult> RemoveAsync(string key, UserPreferenceService preferences)
    {
        await preferences.RemoveAsync(key);
        return TypedResults.NoContent();
    }
}

public record PreferenceRequest(string Value);
