using Souffleur.Domain.Common;
using Souffleur.Domain.Contacts;
using Souffleur.Domain.Identity;

namespace Souffleur.Domain.Mail;

/// <summary>
/// Eine E-Mail des Hauses -- geschrieben aus souffleur heraus oder im
/// Postfach des Hauses angekommen.
/// </summary>
/// <remarks>
/// Das Haus hat **ein** Postfach, nicht jede Person ihres. Was dort ankommt,
/// betrifft das Haus: Anfragen, Rechnungen, Absagen. Deshalb sieht jede
/// angemeldete Person dieselben Mails, und wer eine schreibt, schreibt im
/// Namen des Hauses -- <see cref="SenderUserId"/> haelt fest, wer das war.
///
/// Gespeichert wird nur Text. HTML-Mails werden beim Abholen auf ihren Text
/// reduziert, Anhaenge bleiben auf dem Mailserver. souffleur ist kein
/// Mailprogramm; es zeigt, was das Haus erreicht hat, und laesst kurz
/// antworten. Wer mehr will, oeffnet sein Mailprogramm.
///
/// Abgeholte Mails werden nicht geloescht und auf dem Server nicht
/// angefasst: <see cref="MessageId"/> und <see cref="ImapUid"/> sorgen dafuer,
/// dass dieselbe Mail nicht zweimal landet.
/// </remarks>
public class EmailMessage : Entity
{
    public const int MaxSubjectLength = 500;

    public const int MaxBodyLength = 50_000;

    public required MailDirection Direction { get; set; }

    public required string FromAddress { get; set; }

    public string? FromName { get; set; }

    public required string Subject { get; set; }

    public required string Body { get; set; }

    /// <summary>Wann sie rausging oder -- bei empfangenen -- laut Kopfzeile geschrieben wurde.</summary>
    public required DateTimeOffset SentAt { get; set; }

    /// <summary>Nur bei empfangenen: Wer sie zuerst geoeffnet hat, hat sie fuer alle gelesen.</summary>
    public DateTimeOffset? ReadAt { get; set; }

    /// <summary>Bei gesendeten: wer sie aus souffleur geschrieben hat.</summary>
    public Guid? SenderUserId { get; set; }

    public User? SenderUser { get; set; }

    /// <summary>Bei empfangenen: der Kontakt, zu dessen Adresse der Absender passt.</summary>
    public Guid? ContactId { get; set; }

    public Contact? Contact { get; set; }

    /// <summary>Die Message-ID aus der Kopfzeile -- die Sperre gegen Doppeltes beim Abholen.</summary>
    public string? MessageId { get; set; }

    /// <summary>Die laufende Nummer im Postfach, damit das Abholen dort weitermacht, wo es aufgehoert hat.</summary>
    public long? ImapUid { get; set; }

    public List<EmailRecipient> Recipients { get; set; } = [];

    public bool IsUnread => Direction == MailDirection.Incoming && ReadAt is null;

    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];

        if (Recipients.Count == 0)
        {
            problems.Add("Die E-Mail braucht mindestens eine Empfängerin oder einen Empfänger.");
        }

        foreach (EmailRecipient recipient in Recipients.Where(r => !LooksLikeAddress(r.Address)))
        {
            problems.Add($"„{recipient.Address}“ sieht nicht nach einer E-Mail-Adresse aus.");
        }

        if (string.IsNullOrWhiteSpace(Subject))
        {
            problems.Add("Ein Betreff ist nötig -- ohne ihn landet die Mail im Spam.");
        }
        else if (Subject.Length > MaxSubjectLength)
        {
            problems.Add($"Der Betreff ist zu lang; {MaxSubjectLength} Zeichen sind die Grenze.");
        }

        if (string.IsNullOrWhiteSpace(Body))
        {
            problems.Add("Eine leere E-Mail wird nicht gesendet.");
        }
        else if (Body.Length > MaxBodyLength)
        {
            problems.Add($"Der Text ist zu lang; {MaxBodyLength} Zeichen sind die Grenze.");
        }

        return problems;
    }

    /// <summary>
    /// Grob, mit Absicht: Was gueltig ist, entscheidet der Mailserver beim
    /// Senden. Hier wird nur abgefangen, was offensichtlich keine Adresse ist.
    /// </summary>
    public static bool LooksLikeAddress(string address)
    {
        int at = address.IndexOf('@');
        return at > 0 && at < address.Length - 1 && !address.Any(char.IsWhiteSpace);
    }
}

public enum MailDirection
{
    /// <summary>Aus souffleur geschrieben.</summary>
    Outgoing = 1,

    /// <summary>Im Postfach des Hauses angekommen.</summary>
    Incoming = 2,
}

/// <summary>
/// Wer eine Mail bekommen hat. Eigene Zeile und nicht ein Textfeld, damit
/// sich am Kontakt zeigen laesst, was das Haus ihm geschrieben hat.
/// </summary>
public class EmailRecipient : Entity
{
    public Guid EmailMessageId { get; set; }

    public EmailMessage EmailMessage { get; set; } = null!;

    public required string Address { get; set; }

    public string? Name { get; set; }

    public Guid? ContactId { get; set; }

    public Contact? Contact { get; set; }

    public string Display => string.IsNullOrWhiteSpace(Name) ? Address : Name;
}
