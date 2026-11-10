using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Mail;
using Souffleur.Infrastructure.Mail;

namespace Souffleur.Api.Features.Mail;

/// <summary>
/// E-Mail aus der Kommunikationsleiste: schreiben an Kontakte, lesen was im
/// Postfach des Hauses angekommen ist.
/// </summary>
/// <remarks>
/// Kein eigenes Modul und kein eigenes Recht: Wer angemeldet ist, darf im
/// Namen des Hauses schreiben -- wie im Chat. Ob ueberhaupt gesendet oder
/// abgeholt werden kann, sagt <c>/status</c>; die Oberflaeche richtet sich
/// danach.
/// </remarks>
public static class MailEndpoints
{
    public static IEndpointRouteBuilder MapMailEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/mail")
            .WithTags("Mail")
            .RequireAuthorization();

        group.MapGet("/status", Status);
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost("/", SendAsync);
        group.MapPost("/{id:guid}/read", MarkReadAsync);

        return app;
    }

    private static IResult Status(MailService mails)
    {
        MailStatus status = mails.Status();
        return TypedResults.Ok(new MailStatusResponse(status.CanSend, status.SendMode.ToString(), status.CanReceive, status.From));
    }

    private static async Task<IResult> ListAsync(MailService mails, [FromQuery] int take = 50)
    {
        List<EmailMessage> found = await mails.All()
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync();

        int unread = await mails.All().CountAsync(m => m.Direction == MailDirection.Incoming && m.ReadAt == null);

        return TypedResults.Ok(new MailListResponse(unread, [.. found.Select(MailResponse.From)]));
    }

    private static async Task<IResult> GetAsync(Guid id, MailService mails)
    {
        EmailMessage? mail = await mails.All().FirstOrDefaultAsync(m => m.Id == id);
        return mail is null ? TypedResults.NotFound() : TypedResults.Ok(MailResponse.From(mail));
    }

    private static async Task<IResult> SendAsync([FromBody] SendMailRequest request, MailService mails)
    {
        try
        {
            EmailMessage sent = await mails.SendAsync(
                request.Recipients ?? [],
                request.Subject ?? string.Empty,
                request.Body ?? string.Empty);

            return TypedResults.Created($"/api/mail/{sent.Id}", MailResponse.From(sent));
        }
        catch (MailException exception)
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["Mail"] = [.. exception.Problems] },
                title: "Die E-Mail wurde nicht gesendet.");
        }
        catch (Exception exception) when (exception is System.Net.Mail.SmtpException or System.Net.Sockets.SocketException)
        {
            // Der Mailserver hat abgelehnt oder ist nicht erreichbar. Der
            // Text geht an die Person -- sie muss wissen, dass nichts raus ist.
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["Mail"] = [$"Der Mailserver hat nicht angenommen: {exception.Message}"] },
                title: "Die E-Mail wurde nicht gesendet.");
        }
    }

    private static async Task<IResult> MarkReadAsync(Guid id, MailService mails)
    {
        EmailMessage? mail = await mails.MarkReadAsync(id);
        return mail is null ? TypedResults.NotFound() : TypedResults.Ok(MailResponse.From(mail));
    }
}

public record SendMailRequest(List<RecipientDraft>? Recipients, string? Subject, string? Body);

public record MailStatusResponse(bool CanSend, string SendMode, bool CanReceive, string? From);

public record MailListResponse(int Unread, List<MailResponse> Mails);

public record MailRecipientResponse(string Address, string? Name, Guid? ContactId);

public record MailResponse(
    Guid Id,
    string Direction,
    string FromAddress,
    string? FromName,
    Guid? ContactId,
    string? SenderName,
    List<MailRecipientResponse> Recipients,
    string Subject,
    string Body,
    DateTimeOffset SentAt,
    DateTimeOffset? ReadAt,
    bool IsUnread)
{
    public static MailResponse From(EmailMessage m) => new(
        m.Id,
        m.Direction == MailDirection.Incoming ? "incoming" : "outgoing",
        m.FromAddress,
        m.FromName ?? m.Contact?.Name,
        m.ContactId,
        m.SenderUser is { } user ? $"{user.GivenName} {user.Surname}".Trim() : null,
        [.. m.Recipients.Select(r => new MailRecipientResponse(r.Address, r.Name, r.ContactId))],
        m.Subject,
        m.Body,
        m.SentAt,
        m.ReadAt,
        m.IsUnread);
}
