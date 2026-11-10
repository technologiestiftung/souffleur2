namespace Souffleur.Infrastructure.Mail;

/// <summary>
/// Das Postfach des Hauses, aus dem souffleur liest. Steht in der .env:
/// <c>Souffleur__Mail__Imap__Host</c>, <c>__Port</c>, <c>__User</c>,
/// <c>__Password</c>, <c>__Folder</c>.
/// </summary>
/// <remarks>
/// Getrennt vom Versand (<c>Souffleur__Mail__Host</c>): Manche Haeuser
/// schicken ueber einen Dienstleister und empfangen bei ihrem Anbieter.
/// Ohne Host wird nichts abgeholt -- gesendet werden kann trotzdem.
///
/// IMAP ueber TLS, also Port 993. Gelesen wird nur; nichts wird geloescht,
/// verschoben oder als gelesen markiert. Das Mailprogramm des Hauses sieht
/// das Postfach danach unveraendert.
/// </remarks>
public class MailboxOptions
{
    public const string Section = "Souffleur:Mail:Imap";

    public string? Host { get; set; }

    public int Port { get; set; } = 993;

    public string? User { get; set; }

    public string? Password { get; set; }

    public string Folder { get; set; } = "INBOX";

    /// <summary>Wie weit das erste Abholen zurueckschaut: Ein Haus, das souffleur einschaltet, will nicht den Rueckstand eines Jahres sehen.</summary>
    public int InitialDays { get; set; } = 30;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
}
