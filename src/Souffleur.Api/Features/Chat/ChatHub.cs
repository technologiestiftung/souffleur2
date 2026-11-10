using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Souffleur.Api.Features.Chat;

/// <summary>
/// Hält die Oberflächen auf Stand, ohne dass sie fragen müssen.
/// </summary>
/// <remarks>
/// Der Hub traegt keine Fachlogik: Wer etwas schreiben darf, entscheidet der
/// <c>ConversationService</c> am Endpunkt. Hier wird nur verteilt, und zwar
/// an die Gruppe der jeweiligen Unterhaltung -- sonst bekaeme jeder im Haus
/// jede Direktnachricht zugestellt.
/// </remarks>
[Authorize]
public class ChatHub : Hub
{
    public static string GroupOf(Guid conversationId) => $"conversation:{conversationId}";

    /// <summary>
    /// Die eigene Leitung eines Kontos. Darueber erfaehrt jemand von
    /// Unterhaltungen, die er gerade nicht offen hat -- eine neue
    /// Direktnachricht, eine Aufnahme in einen Kanal, ein geloeschter Kanal.
    /// </summary>
    public static string GroupOfUser(Guid userId) => $"user:{userId}";

    /// <summary>Sagt den Genannten, dass sich an einer Unterhaltung etwas getan hat.</summary>
    public static Task TellAsync(IHubContext<ChatHub> hub, IEnumerable<Guid> userIds, Guid conversationId) =>
        Task.WhenAll(userIds.Distinct().Select(userId =>
            hub.Clients.Group(GroupOfUser(userId)).SendAsync("conversationChanged", conversationId)));

    public override async Task OnConnectedAsync()
    {
        if (Guid.TryParse(Context.UserIdentifier, out Guid userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupOfUser(userId));
        }

        await base.OnConnectedAsync();
    }

    public Task Join(Guid conversationId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupOf(conversationId));

    public Task Leave(Guid conversationId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupOf(conversationId));
}
