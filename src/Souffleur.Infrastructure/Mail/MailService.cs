using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Souffleur.Domain.Contacts;
using Souffleur.Domain.Mail;
using Souffleur.Infrastructure.Notifications;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Mail;

/// <summary>Ein Empfaenger beim Schreiben: ein Kontakt, oder eine Adresse von Hand.</summary>
public record RecipientDraft(Guid? ContactId, string? Address);

public record MailStatus(MailSendMode SendMode, bool CanReceive, string? From)
{
    public bool CanSend => SendMode != MailSendMode.Off;
}

/// <summary>
/// Die E-Mails des Hauses: schreiben, ablegen, abholen, lesen. Einziger Weg
/// dorthin, damit Versand und Ablage nicht auseinanderlaufen.
/// </summary>
public class MailService(
    SouffleurDbContext context,
    TimeProvider time,
    ICurrentUser currentUser,
    IMailSender sender,
    IMailbox mailbox,
    IConfiguration configuration)
{
    public MailStatus Status()
    {
        var options = configuration.GetSection(MailOptions.Section).Get<MailOptions>() ?? new MailOptions();
        return new MailStatus(sender.Mode, mailbox.IsConfigured, options.From);
    }

    /// <summary>Alle Mails des Hauses, neueste zuerst. Gesendete und empfangene in einer Liste -- so liest sich ein Schriftwechsel.</summary>
    public IQueryable<EmailMessage> All() =>
        context.EmailMessages
            .Include(m => m.Recipients)
            .Include(m => m.Contact)
            .Include(m => m.SenderUser)
            .OrderByDescending(m => m.SentAt);

    /// <summary>
    /// Schreibt im Namen des Hauses. Erst wird verschickt, dann abgelegt: Eine
    /// Mail, die der Server abgelehnt hat, soll nicht als gesendet dastehen.
    /// </summary>
    public async Task<EmailMessage> SendAsync(
        IReadOnlyList<RecipientDraft> to,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        Guid me = currentUser.Id ?? throw new MailException(["Nicht angemeldet."]);

        List<EmailRecipient> recipients = [];
        List<string> problems = [];
        foreach (RecipientDraft draft in to)
        {
            if (draft.ContactId is { } contactId)
            {
                Contact? contact = await context.Contacts.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == contactId, cancellationToken);
                if (contact is null)
                {
                    problems.Add("Einer der Kontakte ist nicht mehr da.");
                }
                else if (string.IsNullOrWhiteSpace(contact.Email))
                {
                    problems.Add($"Für „{contact.Name}“ ist keine E-Mail-Adresse hinterlegt.");
                }
                else
                {
                    recipients.Add(new EmailRecipient { Address = contact.Email.Trim(), Name = contact.Name, ContactId = contact.Id });
                }
            }
            else if (!string.IsNullOrWhiteSpace(draft.Address))
            {
                string address = draft.Address.Trim();
                Contact? bekannt = await ByAddressAsync(address, cancellationToken);
                recipients.Add(new EmailRecipient { Address = address, Name = bekannt?.Name, ContactId = bekannt?.Id });
            }
        }

        if (problems.Count > 0)
        {
            throw new MailException(problems);
        }

        MailStatus status = Status();
        var mail = new EmailMessage
        {
            Direction = MailDirection.Outgoing,
            FromAddress = status.From ?? "souffleur@localhost",
            FromName = null,
            Subject = subject.Trim(),
            Body = body.Trim(),
            SentAt = time.GetUtcNow(),
            SenderUserId = me,
            Recipients = recipients,
        };

        IReadOnlyList<string> invalid = mail.Validate();
        if (invalid.Count > 0)
        {
            throw new MailException(invalid);
        }

        if (!status.CanSend)
        {
            throw new MailException(["In diesem Haus ist kein Mailserver eingetragen -- senden geht noch nicht."]);
        }

        await sender.SendAsync([.. recipients.Select(r => r.Address)], mail.Subject, mail.Body, cancellationToken);

        context.EmailMessages.Add(mail);
        await context.SaveChangesAsync(cancellationToken);

        // Die Antwort zeigt, wer geschrieben hat -- das Konto nachladen.
        await context.Entry(mail).Reference(m => m.SenderUser).LoadAsync(cancellationToken);
        return mail;
    }

    public async Task<EmailMessage?> MarkReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        EmailMessage? mail = await All().FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (mail is null)
        {
            return null;
        }

        if (mail.IsUnread)
        {
            mail.ReadAt = time.GetUtcNow();
            await context.SaveChangesAsync(cancellationToken);
        }

        return mail;
    }

    public Task<long?> LastImapUidAsync(CancellationToken cancellationToken = default) =>
        context.EmailMessages
            .Where(m => m.ImapUid != null)
            .MaxAsync(m => m.ImapUid, cancellationToken);

    /// <summary>
    /// Legt abgeholte Mails ab und haengt sie an den Kontakt, dessen Adresse
    /// zum Absender passt. Was schon da ist -- gleiche Message-ID oder
    /// gleiche UID --, wird uebersprungen.
    /// </summary>
    public async Task<int> StoreIncomingAsync(IReadOnlyList<IncomingMail> incoming, CancellationToken cancellationToken = default)
    {
        int count = 0;
        foreach (IncomingMail mail in incoming)
        {
            bool bekannt = await context.EmailMessages.AnyAsync(
                m => m.ImapUid == mail.Uid || (mail.MessageId != null && m.MessageId == mail.MessageId),
                cancellationToken);
            if (bekannt)
            {
                continue;
            }

            Contact? absender = await ByAddressAsync(mail.FromAddress, cancellationToken);

            context.EmailMessages.Add(new EmailMessage
            {
                Direction = MailDirection.Incoming,
                FromAddress = mail.FromAddress,
                FromName = mail.FromName ?? absender?.Name,
                Subject = mail.Subject.Length > EmailMessage.MaxSubjectLength
                    ? mail.Subject[..EmailMessage.MaxSubjectLength]
                    : mail.Subject,
                Body = mail.Body.Length > EmailMessage.MaxBodyLength
                    ? mail.Body[..EmailMessage.MaxBodyLength] + "\n[… gekürzt]"
                    : mail.Body,
                SentAt = mail.Date,
                ContactId = absender?.Id,
                MessageId = mail.MessageId,
                ImapUid = mail.Uid,
                Recipients = [.. mail.To.Select(t => new EmailRecipient { Address = t.Address, Name = t.Name })],
            });
            count++;
        }

        if (count > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return count;
    }

    /// <summary>Der Kontakt zu einer Adresse, Gross- und Kleinschreibung egal.</summary>
    private Task<Contact?> ByAddressAsync(string address, CancellationToken cancellationToken)
    {
        string gesucht = address.Trim().ToLowerInvariant();
        return context.Contacts.AsNoTracking()
            .Where(c => c.Email != null && c.Email.ToLower() == gesucht)
            .OrderByDescending(c => c.IsActive)
            .FirstOrDefaultAsync(cancellationToken);
    }
}

public class MailException(IReadOnlyList<string> problems)
    : InvalidOperationException(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}
