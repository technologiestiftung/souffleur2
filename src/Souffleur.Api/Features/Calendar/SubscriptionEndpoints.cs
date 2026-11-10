using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using Souffleur.Api.Infrastructure;
using Souffleur.Domain.Calendar;
using Souffleur.Domain.Identity;
using Souffleur.Domain.Modules;
using Souffleur.Infrastructure.Calendar;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Api.Features.Calendar;

/// <summary>
/// Der Kalender zum Abonnieren -- siehe ADR-0006.
/// </summary>
/// <remarks>
/// Eine Adresse, die sich in Apple Kalender, Outlook, Google oder Thunderbird
/// eintragen lässt. Ändert sich in souffleur ein Termin, ändert er sich dort
/// beim nächsten Abgleich. Keine Datei zum Herunterladen: Die wäre in dem
/// Moment veraltet, in dem die Disposition etwas verschiebt.
/// </remarks>
public static class SubscriptionEndpoints
{
    public static IEndpointRouteBuilder MapSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/calendar")
            .WithTags("Kalender abonnieren")
            .RequireModule(SouffleurModule.Calendar);

        // Ohne Anmeldung, mit Absicht: Eine Kalenderanwendung kann sich nicht
        // anmelden. Die Berechtigung steckt im Token in der Adresse.
        group.MapGet("/feed/{token}.ics", FeedAsync).AllowAnonymous();

        group.MapGet("/subscriptions", ListAsync).RequireAuthorization();
        group.MapPost("/subscriptions", CreateAsync).RequireAuthorization();
        group.MapPut("/subscriptions/{id:guid}", UpdateAsync).RequireAuthorization();
        group.MapPost("/subscriptions/{id:guid}/rotate", RotateAsync).RequireAuthorization();
        group.MapDelete("/subscriptions/{id:guid}", DeleteAsync).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> FeedAsync(
        string token,
        SubscriptionService subscriptions,
        TimeProvider time,
        HttpContext http,
        CancellationToken cancellation)
    {
        IcsCalendar? calendar = await subscriptions.FeedAsync(token, cancellation);

        if (calendar is null)
        {
            // Dieselbe Antwort für „gibt es nicht" und „zurückgezogen": Wer
            // Adressen durchprobiert, soll am Statuscode nichts ablesen können.
            return TypedResults.NotFound();
        }

        string body = IcsWriter.Build(calendar, time.GetUtcNow());

        // Das Etikett spart den Kalenderanwendungen den Abgleich: Hat sich
        // nichts geändert, antwortet der Server mit 304 und ohne Inhalt.
        string tag = $"\"{Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(body)))[..32].ToLowerInvariant()}\"";

        if (http.Request.Headers.IfNoneMatch.Contains(tag))
        {
            return TypedResults.StatusCode(StatusCodes.Status304NotModified);
        }

        http.Response.Headers[HeaderNames.ETag] = tag;
        http.Response.Headers[HeaderNames.CacheControl] = "private, max-age=300";
        http.Response.Headers[HeaderNames.ContentDisposition] = "inline; filename=\"souffleur.ics\"";

        return TypedResults.Text(body, "text/calendar; charset=utf-8");
    }

    private static async Task<IResult> ListAsync(
        SubscriptionService subscriptions,
        ICurrentUser user,
        ClaimsPrincipal principal,
        HttpContext http,
        CancellationToken token)
    {
        List<CalendarSubscription> found = await subscriptions.ListAsync(
            user.Id, principal.IsInRole(Roles.Administrator), token);

        return TypedResults.Ok(
            found.Select(s => SubscriptionResponse.From(s, Address(http, s.Token))));
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] SubscriptionRequest request,
        SubscriptionService subscriptions,
        ICurrentUser user,
        HttpContext http,
        CancellationToken token)
    {
        var subscription = new CalendarSubscription
        {
            Name = request.Name.Trim(),
            Token = string.Empty,
            OwnerId = user.Id,
        };
        Apply(request, subscription);

        try
        {
            await subscriptions.AddAsync(subscription, token);
        }
        catch (CalendarException exception)
        {
            return Rejected(exception);
        }

        return TypedResults.Created(
            $"/api/calendar/subscriptions/{subscription.Id}",
            SubscriptionResponse.From(subscription, Address(http, subscription.Token)));
    }

    private static Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] SubscriptionRequest request,
        SubscriptionService subscriptions,
        ICurrentUser user,
        ClaimsPrincipal principal,
        HttpContext http,
        CancellationToken token) =>
        OwnedAsync(id, subscriptions, user, principal, http, token, async () =>
            SubscriptionResponse.From(
                await subscriptions.UpdateAsync(id, found =>
                {
                    found.Name = request.Name.Trim();
                    Apply(request, found);
                }, token),
                string.Empty));

    private static Task<IResult> RotateAsync(
        Guid id,
        SubscriptionService subscriptions,
        ICurrentUser user,
        ClaimsPrincipal principal,
        HttpContext http,
        CancellationToken token) =>
        OwnedAsync(id, subscriptions, user, principal, http, token, async () =>
            SubscriptionResponse.From(await subscriptions.RotateAsync(id, token), string.Empty));

    private static async Task<IResult> DeleteAsync(
        Guid id,
        SubscriptionService subscriptions,
        ICurrentUser user,
        ClaimsPrincipal principal,
        SouffleurDbContext context,
        CancellationToken token)
    {
        if (!await MayChangeAsync(id, user, principal, context, token))
        {
            return TypedResults.NotFound();
        }

        try
        {
            await subscriptions.DeleteAsync(id, token);
        }
        catch (CalendarException exception)
        {
            return Rejected(exception);
        }

        return TypedResults.NoContent();
    }

    /// <summary>
    /// Ändern darf nur, wem das Abonnement gehört -- oder wer das Haus
    /// einrichtet.
    /// </summary>
    private static async Task<IResult> OwnedAsync(
        Guid id,
        SubscriptionService subscriptions,
        ICurrentUser user,
        ClaimsPrincipal principal,
        HttpContext http,
        CancellationToken token,
        Func<Task<SubscriptionResponse>> change)
    {
        SouffleurDbContext context = http.RequestServices.GetRequiredService<SouffleurDbContext>();

        if (!await MayChangeAsync(id, user, principal, context, token))
        {
            return TypedResults.NotFound();
        }

        try
        {
            SubscriptionResponse changed = await change();
            return TypedResults.Ok(changed with { Address = Address(http, changed.Token) });
        }
        catch (CalendarException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<bool> MayChangeAsync(
        Guid id,
        ICurrentUser user,
        ClaimsPrincipal principal,
        SouffleurDbContext context,
        CancellationToken token)
    {
        if (principal.IsInRole(Roles.Administrator))
        {
            return true;
        }

        Guid? owner = await context.CalendarSubscriptions
            .Where(s => s.Id == id)
            .Select(s => s.OwnerId)
            .FirstOrDefaultAsync(token);

        return owner is not null && owner == user.Id;
    }

    private static void Apply(SubscriptionRequest request, CalendarSubscription subscription)
    {
        subscription.CategoryIds = [.. request.CategoryIds ?? []];
        subscription.ResourceIds = [.. request.ResourceIds ?? []];
        subscription.ContactIds = [.. request.ContactIds ?? []];
        subscription.IncludeCancelled = request.IncludeCancelled;

        if (request.IsActive is { } active)
        {
            subscription.IsActive = active;
        }
    }

    /// <summary>Die Adresse, so wie sie einzutragen ist.</summary>
    private static string Address(HttpContext http, string token) =>
        $"{http.Request.Scheme}://{http.Request.Host}/api/calendar/feed/{token}.ics";

    private static IResult Rejected(CalendarException exception) =>
        TypedResults.ValidationProblem(
            new Dictionary<string, string[]> { ["Abonnement"] = [.. exception.Problems] },
            title: "Das Abonnement wurde nicht angenommen.");
}

public record SubscriptionRequest(
    string Name,
    IReadOnlyList<Guid>? CategoryIds = null,
    IReadOnlyList<Guid>? ResourceIds = null,
    IReadOnlyList<Guid>? ContactIds = null,
    bool IncludeCancelled = true,
    bool? IsActive = null);

public record SubscriptionResponse(
    Guid Id,
    string Name,
    string Token,
    string Address,
    IReadOnlyList<Guid> CategoryIds,
    IReadOnlyList<Guid> ResourceIds,
    IReadOnlyList<Guid> ContactIds,
    bool IncludeCancelled,
    bool IsActive,
    DateTimeOffset? LastFetchedAt,
    int FetchCount)
{
    public static SubscriptionResponse From(CalendarSubscription s, string address) => new(
        s.Id, s.Name, s.Token, address, s.CategoryIds, s.ResourceIds, s.ContactIds,
        s.IncludeCancelled, s.IsActive, s.LastFetchedAt, s.FetchCount);
}
