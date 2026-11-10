using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Souffleur.Domain.Notifications;
using Souffleur.Infrastructure.Notifications;

namespace Souffleur.Api.Features.Notifications;

/// <summary>
/// Der Kanal der Glocke. Eigener Hub und nicht der des Chats: Der Chat
/// verbindet sich nur, wenn seine Leiste offen ist -- die Glocke soll immer
/// klingeln.
/// </summary>
/// <remarks>
/// Zugestellt wird an die Person, nicht an eine Gruppe von Verbindungen.
/// SignalR kennt sie ueber die Kennung im Anmeldecookie; wer in zwei Tabs
/// angemeldet ist, bekommt es in beiden.
/// </remarks>
[Authorize]
public class NotificationHub : Hub;

public class SignalRNotificationPusher(IHubContext<NotificationHub> hub) : INotificationPusher
{
    public Task PushAsync(Guid userId, Notification notification, CancellationToken cancellationToken = default) =>
        hub.Clients.User(userId.ToString())
           .SendAsync("notification", NotificationResponse.From(notification), cancellationToken);
}

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/notifications")
                                     .WithTags("Benachrichtigungen")
                                     .RequireAuthorization();

        group.MapGet("/", MineAsync);
        group.MapPost("/{id:guid}/read", ReadAsync);
        group.MapPost("/read-all", ReadAllAsync);
        group.MapGet("/settings", SettingsAsync);
        group.MapPut("/settings", SaveSettingsAsync);

        app.MapHub<NotificationHub>("/hubs/notifications").RequireAuthorization();
        return app;
    }

    private static async Task<IResult> MineAsync(NotificationService notifications)
    {
        (List<Notification> items, int unread) = await notifications.MineAsync();
        return TypedResults.Ok(new NotificationsResponse([.. items.Select(NotificationResponse.From)], unread));
    }

    private static async Task<IResult> ReadAsync(Guid id, NotificationService notifications)
    {
        await notifications.MarkReadAsync(id);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ReadAllAsync(NotificationService notifications)
    {
        await notifications.MarkAllReadAsync();
        return TypedResults.NoContent();
    }

    private static async Task<IResult> SettingsAsync(NotificationService notifications, IConfiguration configuration)
    {
        Dictionary<NotificationKind, EmailMode> settings = await notifications.SettingsAsync();
        bool mailBereit = configuration.GetSection(MailOptions.Section).Get<MailOptions>()?.IsConfigured ?? false;

        return TypedResults.Ok(new SettingsResponse(
            mailBereit,
            [.. settings.Select(s => new SettingResponse(s.Key, s.Key.ToString(), s.Key.Label(), s.Value))]));
    }

    private static async Task<IResult> SaveSettingsAsync(
        [FromBody] SettingRequest[] request, NotificationService notifications)
    {
        if (request.Any(r => !Enum.IsDefined(r.Kind) || !Enum.IsDefined(r.Email)))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["settings"] = ["Eine der Einstellungen ist nicht lesbar."],
            });
        }

        await notifications.SaveSettingsAsync(request.ToDictionary(r => r.Kind, r => r.Email));
        return TypedResults.NoContent();
    }
}

public record NotificationResponse(
    Guid Id, NotificationKind Kind, string Title, string? Body, string? Link, DateTimeOffset CreatedAt, bool IsRead)
{
    public static NotificationResponse From(Notification n) =>
        new(n.Id, n.Kind, n.Title, n.Body, n.Link, n.CreatedAt, n.IsRead);
}

public record NotificationsResponse(NotificationResponse[] Items, int Unread);

/// <param name="MailReady">Ob das Haus einen Mailserver eingetragen hat -- sonst bringt die Wahl nichts.</param>
public record SettingsResponse(bool MailReady, SettingResponse[] Settings);

public record SettingResponse(NotificationKind Kind, string Key, string Label, EmailMode Email);

public record SettingRequest(NotificationKind Kind, EmailMode Email);
