using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using Microsoft.Extensions.Configuration;
using System.Text;
using MimeKit;
using MimeKit.Text;

namespace Souffleur.Infrastructure.Mail;

/// <summary>Eine angekommene Mail, so wie sie aus dem Postfach kommt -- noch ohne Bezug zu einem Kontakt.</summary>
public record IncomingMail(
    long Uid,
    string? MessageId,
    string FromAddress,
    string? FromName,
    string Subject,
    string Body,
    DateTimeOffset Date,
    IReadOnlyList<(string Address, string? Name)> To);

/// <summary>Liest das Postfach. Eigene Schnittstelle, damit Tests ohne Mailserver auskommen.</summary>
public interface IMailbox
{
    bool IsConfigured { get; }

    /// <summary>Alles, was nach <paramref name="afterUid"/> angekommen ist; ohne Angabe die letzten Tage.</summary>
    Task<IReadOnlyList<IncomingMail>> FetchNewAsync(long? afterUid, CancellationToken cancellationToken = default);
}

public class ImapMailbox(IConfiguration configuration, TimeProvider time) : IMailbox
{
    /// <summary>Je Durchlauf hoechstens so viele -- der Rest kommt beim naechsten.</summary>
    private const int Hoechstens = 50;

    private MailboxOptions Options =>
        configuration.GetSection(MailboxOptions.Section).Get<MailboxOptions>() ?? new MailboxOptions();

    public bool IsConfigured => Options.IsConfigured;

    public async Task<IReadOnlyList<IncomingMail>> FetchNewAsync(long? afterUid, CancellationToken cancellationToken = default)
    {
        MailboxOptions options = Options;
        if (!options.IsConfigured)
        {
            return [];
        }

        using var client = new ImapClient();
        await client.ConnectAsync(options.Host!, options.Port, MailKit.Security.SecureSocketOptions.SslOnConnect, cancellationToken);
        if (!string.IsNullOrEmpty(options.User))
        {
            await client.AuthenticateAsync(options.User, options.Password ?? string.Empty, cancellationToken);
        }

        IMailFolder folder = options.Folder == "INBOX"
            ? client.Inbox
            : await client.GetFolderAsync(options.Folder, cancellationToken);
        await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

        SearchQuery query = afterUid is { } letzte
            ? SearchQuery.Uids(new UniqueIdRange(new UniqueId((uint)letzte + 1), UniqueId.MaxValue))
            : SearchQuery.DeliveredAfter(time.GetUtcNow().AddDays(-options.InitialDays).DateTime);

        IList<UniqueId> uids = await folder.SearchAsync(query, cancellationToken);
        List<IncomingMail> result = [];

        // Die Suche nach UIDs liefert auch die letzte bekannte noch einmal,
        // wenn der Server den Bereich grosszuegig auslegt -- deshalb nochmal filtern.
        foreach (UniqueId uid in uids.Where(u => afterUid is null || u.Id > afterUid).OrderBy(u => u.Id).Take(Hoechstens))
        {
            MimeMessage message = await folder.GetMessageAsync(uid, cancellationToken);
            MailboxAddress? from = message.From.Mailboxes.FirstOrDefault();
            if (from is null)
            {
                continue;
            }

            result.Add(new IncomingMail(
                uid.Id,
                string.IsNullOrWhiteSpace(message.MessageId) ? null : message.MessageId,
                from.Address,
                string.IsNullOrWhiteSpace(from.Name) ? null : from.Name,
                message.Subject ?? string.Empty,
                Text(message),
                message.Date,
                [.. message.To.Mailboxes.Select(m => (m.Address, string.IsNullOrWhiteSpace(m.Name) ? null : m.Name))]));
        }

        await client.DisconnectAsync(quit: true, cancellationToken);
        return result;
    }

    /// <summary>
    /// Der Text der Mail. Reiner Text, wenn es ihn gibt; sonst das HTML auf
    /// seinen Text reduziert. souffleur zeigt kein HTML an.
    /// </summary>
    internal static string Text(MimeMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.TextBody))
        {
            return message.TextBody.Trim();
        }

        if (!string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            return HtmlZuText(message.HtmlBody);
        }

        return string.Empty;
    }

    /// <summary>
    /// Grob und bewusst einfach: Tags weg, Absaetze und Zeilenumbrueche
    /// bleiben als Zeilen, Stil und Skript fliegen ganz raus. Mehr braucht
    /// es nicht, um zu sehen, was jemand wollte.
    /// </summary>
    public static string HtmlZuText(string html)
    {
        var text = new StringBuilder();
        var tokenizer = new HtmlTokenizer(new StringReader(html));
        bool unsichtbar = false;

        while (tokenizer.ReadNextToken(out HtmlToken? token))
        {
            switch (token)
            {
                case HtmlTagToken tag when tag.Id is HtmlTagId.Style or HtmlTagId.Script or HtmlTagId.Head:
                    unsichtbar = !tag.IsEndTag;
                    break;
                case HtmlTagToken tag when tag.Id is HtmlTagId.Br or HtmlTagId.P or HtmlTagId.Div or HtmlTagId.TR
                    or HtmlTagId.LI or HtmlTagId.H1 or HtmlTagId.H2 or HtmlTagId.H3 or HtmlTagId.H4 or HtmlTagId.Table:
                    text.Append('\n');
                    break;
                case HtmlDataToken data when !unsichtbar:
                    text.Append(data.Data);
                    break;
            }
        }

        // Entitaeten wie &amp; und &nbsp; sind im Datenstrom noch roh.
        string entschluesselt = System.Net.WebUtility.HtmlDecode(text.ToString()).Replace('\u00A0', ' ');
        IEnumerable<string> zeilen = entschluesselt
            .Split('\n')
            .Select(z => z.Trim())
            .Where((z, i) => z.Length > 0 || i == 0);
        return string.Join('\n', zeilen).Trim();
    }
}
