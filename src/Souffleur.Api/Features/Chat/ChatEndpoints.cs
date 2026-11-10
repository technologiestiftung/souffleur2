using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Souffleur.Api.Infrastructure;
using Souffleur.Domain.Conversations;
using Souffleur.Domain.Identity;
using Souffleur.Domain.Modules;
using Souffleur.Infrastructure.Identity;
using Souffleur.Infrastructure.Conversations;
using Souffleur.Api.Features.Notifications;
using Souffleur.Infrastructure.Notifications;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Api.Features.Chat;

/// <summary>
/// Chat: Kanäle, Direktnachrichten und Unterhaltungen an Gegenständen.
/// </summary>
public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/chat")
            .WithTags("Chat")
            .RequireAuthorization()
            .RequireModule(SouffleurModule.Chat);

        group.MapGet("/conversations", ListAsync);
        group.MapGet("/conversations/{id:guid}/messages", MessagesAsync);
        group.MapPost("/conversations", CreateChannelAsync);
        group.MapPost("/conversations/{id:guid}/messages", PostAsync);
        group.MapPost("/conversations/{id:guid}/read", MarkReadAsync);
        group.MapPost("/conversations/{id:guid}/archive", ArchiveAsync);
        group.MapPost("/conversations/{id:guid}/unarchive", UnarchiveAsync);
        group.MapDelete("/conversations/{id:guid}", DeleteAsync);
        group.MapPost("/messages/{id:guid}/retract", RetractAsync);

        // Wer dabei ist: Gruppenchats entstehen, indem man Personen oder eine
        // ganze Gruppe aufnimmt; Verlassen und Herausnehmen laufen über
        // denselben Weg.
        group.MapGet("/conversations/{id:guid}/participants", ParticipantsAsync);
        group.MapPost("/conversations/{id:guid}/participants", AddParticipantsAsync);
        group.MapDelete("/conversations/{id:guid}/participants/{userId:guid}", RemoveParticipantAsync);
        group.MapPost("/conversations/direct/{userId:guid}", DirectAsync);

        // Die Konten des Hauses zum Auswählen -- für Teilnehmer und für die
        // Vorschlagsliste hinter dem @. Ohne Verwaltungsrecht, weil jeder im
        // Haus jeden ansprechen darf.
        group.MapGet("/people", PeopleAsync);

        // Der Einstieg für jedes andere Modul: die Unterhaltung zu einem
        // Gegenstand holen, und sie entsteht beim ersten Mal.
        group.MapPost("/subjects/{subject}/{subjectId:guid}", ForSubjectAsync);

        app.MapHub<ChatHub>("/hubs/chat").RequireAuthorization();

        return app;
    }

    private static async Task<IResult> ListAsync(
        ConversationService conversations,
        [FromQuery] bool includeArchived = false)
    {
        IQueryable<Conversation> query = conversations.Visible();

        if (!includeArchived)
        {
            query = query.Where(c => c.ArchivedAt == null);
        }

        List<Conversation> found = await query
            .Include(c => c.Participants).ThenInclude(p => p.User)
            .OrderByDescending(c => c.LastMessageAt)
            .Take(200)
            .ToListAsync();

        IReadOnlyDictionary<Guid, int> unread = await conversations.UnreadAsync();

        return TypedResults.Ok(found.Select(c => ConversationResponse.From(
            c, unread.TryGetValue(c.Id, out int count) ? count : 0)));
    }

    private static async Task<IResult> PeopleAsync(
        ConversationService conversations,
        SouffleurDbContext context,
        IConfiguration configuration,
        HttpContext http)
    {
        // Nur auf der Vorfuehrinstanz: Konten anderer Haeuser bleiben verborgen.
        HashSet<Guid> verborgen = await DemoHouses.HiddenAccountsAsync(
            context, configuration, http.User.Identity?.Name);

        return TypedResults.Ok((await conversations.PeopleAsync())
            .Where(p => !verborgen.Contains(p.Id))
            .Select(p => new PersonResponse(p.Id, p.Name)));
    }

    private static async Task<IResult> ParticipantsAsync(Guid id, ConversationService conversations)
    {
        try
        {
            return TypedResults.Ok((await conversations.ParticipantsAsync(id))
                .Select(p => new ParticipantResponse(p.UserId, p.User.DisplayName, p.JoinedAt)));
        }
        catch (ConversationException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> AddParticipantsAsync(
        Guid id,
        [FromBody] ParticipantsRequest request,
        ConversationService conversations,
        IHubContext<ChatHub> hub)
    {
        IReadOnlyList<Guid> added;
        try
        {
            added = await conversations.AddParticipantsAsync(id, request.UserIds, request.GroupIds);
        }
        catch (ConversationException exception)
        {
            return Rejected(exception);
        }

        // Wer neu dabei ist, bekommt die Unterhaltung in seine Liste, ohne
        // neu zu laden.
        await ChatHub.TellAsync(hub, added, id);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> RemoveParticipantAsync(
        Guid id,
        Guid userId,
        ConversationService conversations,
        IHubContext<ChatHub> hub)
    {
        try
        {
            await conversations.LeaveAsync(id, userId);
        }
        catch (ConversationException exception)
        {
            return Rejected(exception);
        }

        await ChatHub.TellAsync(hub, [userId], id);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> DirectAsync(Guid userId, ConversationService conversations)
    {
        try
        {
            Conversation conversation = await conversations.DirectAsync(userId);
            return TypedResults.Ok(ConversationResponse.From(conversation, 0));
        }
        catch (ConversationException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> MessagesAsync(
        Guid id,
        ConversationService conversations,
        [FromQuery] int take = 100)
    {
        Conversation? conversation = await conversations.Visible()
            .FirstOrDefaultAsync(c => c.Id == id);

        if (conversation is null)
        {
            return TypedResults.NotFound();
        }

        List<Message> messages = await conversations.Visible()
            .Where(c => c.Id == id)
            .SelectMany(c => c.Messages)
            .Include(m => m.Author)
            .OrderByDescending(m => m.CreatedAt)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync();

        messages.Reverse();
        return TypedResults.Ok(messages.Select(MessageResponse.From));
    }

    private static async Task<IResult> CreateChannelAsync(
        [FromBody] ChannelRequest request,
        ConversationService conversations)
    {
        try
        {
            Conversation created = await conversations.CreateChannelAsync(
                request.Name, request.Topic, request.IsPrivate, request.Participants);

            return TypedResults.Created(
                $"/api/chat/conversations/{created.Id}", ConversationResponse.From(created, 0));
        }
        catch (ConversationException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> ForSubjectAsync(
        ConversationSubject subject,
        Guid subjectId,
        ConversationService conversations)
    {
        if (subject == ConversationSubject.None || !Enum.IsDefined(subject))
        {
            return TypedResults.Problem(
                "An diese Art von Gegenstand lässt sich keine Unterhaltung heften.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        Conversation conversation = await conversations.ForSubjectAsync(subject, subjectId);
        return TypedResults.Ok(ConversationResponse.From(conversation, 0));
    }

    private static async Task<IResult> PostAsync(
        Guid id,
        [FromBody] MessageRequest request,
        ConversationService conversations,
        IHubContext<ChatHub> hub,
        NotificationService glocke,
        SouffleurDbContext context)
    {
        Message message;
        try
        {
            message = await conversations.PostAsync(id, request.Text, request.InReplyToId, request.Mentions);
        }
        catch (ConversationException exception)
        {
            return Rejected(exception);
        }

        await hub.Clients.Group(ChatHub.GroupOf(id))
            .SendAsync("messagePosted", MessageResponse.From(message));

        // Wer die Unterhaltung nicht offen hat, hoert auf der eigenen Leitung
        // davon -- sonst bliebe eine neue Direktnachricht unbemerkt, bis man
        // zufaellig neu laedt.
        List<Guid> beteiligte = await context.ConversationParticipants
            .Where(p => p.ConversationId == id && p.LeftAt == null && p.UserId != message.AuthorId)
            .Select(p => p.UserId)
            .ToListAsync();
        await ChatHub.TellAsync(hub, beteiligte, id);

        await BenachrichtigenAsync(id, request, message, glocke, context);

        return TypedResults.Created(
            $"/api/chat/conversations/{id}/messages/{message.Id}", MessageResponse.From(message));
    }

    /// <summary>
    /// Wer erwaehnt wurde, bekommt Bescheid -- aber nur, wer die Unterhaltung
    /// auch lesen darf. In einem geschlossenen Kanal oder einer
    /// Direktnachricht wuerde die Glocke sonst den Text an jemanden tragen,
    /// der ihn nie sehen sollte.
    /// </summary>
    private static async Task BenachrichtigenAsync(
        Guid id, MessageRequest request, Message message, NotificationService glocke, SouffleurDbContext context)
    {
        bool personen = request.Mentions is { Count: > 0 };
        bool gruppen = request.GroupMentions is { Count: > 0 };
        if (!personen && !gruppen)
        {
            return;
        }

        var gespraech = await context.Conversations.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new
            {
                c.Name,
                c.Kind,
                c.IsPrivate,
                Drin = c.Participants.Where(p => p.LeftAt == null).Select(p => p.UserId).ToList(),
            })
            .FirstOrDefaultAsync();
        if (gespraech is null)
        {
            return;
        }

        List<Guid> erwaehnt = [.. request.Mentions ?? []];
        erwaehnt.AddRange(await glocke.UsersOfGroupsAsync(request.GroupMentions ?? []));
        if (gespraech.IsPrivate)
        {
            erwaehnt = [.. erwaehnt.Where(gespraech.Drin.Contains)];
        }

        string wo = gespraech.Kind switch
        {
            ConversationKind.Direct => "einer Direktnachricht",
            ConversationKind.Attached => "einer Unterhaltung",
            _ => $"#{gespraech.Name}",
        };

        string auszug = message.Text.Length > 140 ? message.Text[..139] + "…" : message.Text;
        await Ausloeser.ErwaehntAsync(glocke, erwaehnt, [], wo, auszug, id);
    }

    private static async Task<IResult> RetractAsync(
        Guid id,
        ConversationService conversations,
        IHubContext<ChatHub> hub)
    {
        try
        {
            await conversations.RetractAsync(id);
        }
        catch (ConversationException exception)
        {
            return Rejected(exception);
        }

        await hub.Clients.All.SendAsync("messageRetracted", id);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> MarkReadAsync(Guid id, ConversationService conversations)
    {
        try
        {
            await conversations.MarkReadAsync(id);
        }
        catch (ConversationException exception)
        {
            return Rejected(exception);
        }

        return TypedResults.NoContent();
    }

    private static async Task<IResult> ArchiveAsync(Guid id, ConversationService conversations, IHubContext<ChatHub> hub)
    {
        try
        {
            await conversations.ArchiveAsync(id);
        }
        catch (ConversationException exception)
        {
            return Rejected(exception);
        }

        await hub.Clients.Group(ChatHub.GroupOf(id)).SendAsync("conversationChanged", id);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> UnarchiveAsync(Guid id, ConversationService conversations, IHubContext<ChatHub> hub)
    {
        try
        {
            await conversations.UnarchiveAsync(id);
        }
        catch (ConversationException exception)
        {
            return Rejected(exception);
        }

        await hub.Clients.Group(ChatHub.GroupOf(id)).SendAsync("conversationChanged", id);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        ClaimsPrincipal principal,
        ConversationService conversations,
        SouffleurDbContext context,
        IHubContext<ChatHub> hub)
    {
        // Vorher einsammeln: Nach dem Loeschen gibt es die Teilnehmer nicht mehr.
        List<Guid> beteiligte = await context.ConversationParticipants
            .Where(p => p.ConversationId == id)
            .Select(p => p.UserId)
            .ToListAsync();

        bool verwaltung = principal.HasClaim(
            PermissionClaimsFactory.ClaimType, ((int)Permission.HouseManage).ToString());

        try
        {
            await conversations.DeleteAsync(id, verwaltung);
        }
        catch (ConversationException exception)
        {
            return Rejected(exception);
        }

        await ChatHub.TellAsync(hub, beteiligte, id);
        return TypedResults.NoContent();
    }

    private static IResult Rejected(ConversationException exception) =>
        TypedResults.ValidationProblem(
            new Dictionary<string, string[]> { ["Chat"] = [.. exception.Problems] },
            title: "Die Nachricht wurde nicht angenommen.");
}

public record ChannelRequest(
    string Name,
    string? Topic = null,
    bool IsPrivate = false,
    IReadOnlyList<Guid>? Participants = null);

/// <param name="GroupMentions">Erwaehnte Gruppen -- „@Technik" meint alle, die drin sind.</param>
public record MessageRequest(
    string Text,
    Guid? InReplyToId = null,
    IReadOnlyList<Guid>? Mentions = null,
    IReadOnlyList<Guid>? GroupMentions = null);

public record ParticipantsRequest(
    IReadOnlyList<Guid>? UserIds = null,
    IReadOnlyList<Guid>? GroupIds = null);

public record PersonResponse(Guid Id, string Name);

public record ParticipantResponse(Guid UserId, string Name, DateTimeOffset JoinedAt);

/// <param name="Participants">Wer dabei ist -- daraus wird der Titel einer Direktnachricht.</param>
/// <param name="CreatedBy">Wer angelegt hat, darf loeschen.</param>
public record ConversationResponse(
    Guid Id,
    ConversationKind Kind,
    string? Name,
    string? Topic,
    ConversationSubject SubjectType,
    string SubjectLabel,
    Guid? SubjectId,
    bool IsPrivate,
    bool IsArchived,
    DateTimeOffset? LastMessageAt,
    int Unread,
    IReadOnlyList<PersonResponse> Participants,
    Guid? CreatedBy)
{
    public static ConversationResponse From(Conversation c, int unread) => new(
        c.Id, c.Kind, c.Name, c.Topic, c.SubjectType, c.SubjectType.Label(), c.SubjectId,
        c.IsPrivate, c.IsArchived, c.LastMessageAt, unread,
        [.. c.Participants
            .Where(p => p.LeftAt == null && p.User is not null)
            .Select(p => new PersonResponse(p.UserId, p.User.DisplayName))],
        c.CreatedBy);
}

public record MessageResponse(
    Guid Id,
    Guid ConversationId,
    Guid AuthorId,
    string AuthorName,
    string Text,
    bool IsRetracted,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,
    Guid? InReplyToId)
{
    public static MessageResponse From(Message m) => new(
        m.Id, m.ConversationId, m.AuthorId, m.Author?.DisplayName ?? string.Empty,
        m.IsRetracted ? string.Empty : m.Text, m.IsRetracted, m.CreatedAt, m.EditedAt, m.InReplyToId);
}
