using Microsoft.AspNetCore.Mvc;
using Souffleur.Api.Infrastructure;
using Souffleur.Domain.Identity;
using Souffleur.Domain.Modules;
using Souffleur.Domain.Ticketing;
using Souffleur.Infrastructure.Ticketing;
using Souffleur.Infrastructure.Identity;

namespace Souffleur.Api.Features.Ticketing;

/// <summary>
/// Der Vorverkauf des Hauses.
/// </summary>
/// <remarks>
/// Die Einstellungen liegen hinter dem Modul Ticketing: Ein Haus, das nicht
/// selbst verkauft, schaltet es ab und sieht davon nichts mehr. Lesen darf
/// jeder Angemeldete, einrichten nur die Verwaltung -- dort haengt ein Token
/// dran.
/// </remarks>
public static class TicketingEndpoints
{
    public static IEndpointRouteBuilder MapTicketingEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/ticketing")
            .WithTags("Ticketing")
            .RequireModule(SouffleurModule.Ticketing);

        group.MapGet("/settings", SettingsAsync).RequireAuthorization();
        group.MapPut("/settings", SaveAsync).RequireAuthorization(Manage);
        group.MapPost("/check", CheckAsync).RequireAuthorization(Manage);
        group.MapGet("/performances", PerformancesAsync).RequireAuthorization();

        return app;
    }

    private static void Manage(Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder policy) =>
        policy.RequireClaim(PermissionClaimsFactory.ClaimType, ((int)Permission.TicketingManage).ToString());

    private static async Task<IResult> SettingsAsync(TicketingService ticketing) =>
        TypedResults.Ok(TicketingResponse.From(await ticketing.SettingsAsync()));

    private static async Task<IResult> SaveAsync(
        [FromBody] TicketingRequest request,
        TicketingService ticketing)
    {
        try
        {
            TicketingSettings saved = await ticketing.SaveAsync(
                request.Provider,
                request.BaseUrl,
                request.Organizer,
                request.Token,
                request.CacheSeconds);

            return TypedResults.Ok(TicketingResponse.From(saved));
        }
        catch (TicketingException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> CheckAsync(TicketingService ticketing)
    {
        try
        {
            return TypedResults.Ok(TicketingResponse.From(await ticketing.CheckAsync()));
        }
        catch (TicketingException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> PerformancesAsync(TicketingService ticketing)
    {
        try
        {
            IReadOnlyList<Performance> found = await ticketing.PerformancesAsync();
            return TypedResults.Ok(found);
        }
        catch (PretixException exception)
        {
            // Eine fremde Instanz, die nicht antwortet, ist kein Serverfehler
            // hier -- die Seite soll den Grund zeigen koennen.
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["Vorverkauf"] = [exception.Message] },
                title: "Der Vorverkauf antwortet nicht.");
        }
    }

    private static IResult Rejected(TicketingException exception) =>
        TypedResults.ValidationProblem(
            new Dictionary<string, string[]> { ["Vorverkauf"] = [.. exception.Problems] },
            title: "Die Einstellungen wurden nicht angenommen.");
}

/// <param name="Token">
/// Null laesst das hinterlegte Token stehen. Die Oberflaeche bekommt es nie zu
/// sehen und kann es deshalb auch nicht zurueckschicken.
/// </param>
public record TicketingRequest(
    TicketingProvider Provider,
    string? BaseUrl = null,
    string? Organizer = null,
    string? Token = null,
    int CacheSeconds = 60);

public record TicketingResponse(
    TicketingProvider Provider,
    string Key,
    string? BaseUrl,
    string? Organizer,

    /// <summary>Ob ein Token hinterlegt ist -- nicht welches.</summary>
    bool HasToken,
    int CacheSeconds,
    bool IsConfigured,
    DateTimeOffset? LastCheckedAt,
    string? LastProblem)
{
    public static TicketingResponse From(TicketingSettings settings) => new(
        settings.Provider,
        settings.Provider.ToString(),
        settings.BaseUrl,
        settings.Organizer,
        !string.IsNullOrWhiteSpace(settings.Token),
        settings.CacheSeconds,
        settings.IsConfigured,
        settings.LastCheckedAt,
        settings.LastProblem);
}
