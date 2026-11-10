using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Souffleur.Infrastructure.Mail;

/// <summary>
/// Holt alle zwei Minuten ab, was im Postfach des Hauses neu ist.
/// </summary>
/// <remarks>
/// Wo er weitermacht, steht in der Datenbank (die hoechste bekannte UID),
/// nicht im Speicher: Ein Neustart holt nichts doppelt. Beim allerersten
/// Mal gibt es keine UID, dann zaehlen die letzten Tage.
///
/// Der erste Durchlauf wartet kurz, damit die Migrationen durch sind, bevor
/// er die Tabelle fragt -- derselbe Grund, aus dem der Benachrichtigungs-
/// versand beim ersten Start einen Fehler ins Protokoll schreibt.
/// </remarks>
public class MailboxPoller(
    IServiceScopeFactory scopes,
    IMailbox mailbox,
    TimeProvider time,
    ILogger<MailboxPoller> logger) : BackgroundService
{
    private static readonly TimeSpan Takt = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Anlauf = TimeSpan.FromSeconds(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!mailbox.IsConfigured)
        {
            logger.LogInformation("Kein Postfach eingetragen -- empfangene Mails werden nicht abgeholt.");
            return;
        }

        await Task.Delay(Anlauf, time, stoppingToken);

        using var timer = new PeriodicTimer(Takt, time);
        do
        {
            try
            {
                int neu = await DurchlaufAsync(stoppingToken);
                if (neu > 0)
                {
                    logger.LogInformation("Postfach: {Anzahl} neue Mails abgeholt.", neu);
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Postfach: Abholen gescheitert, nächster Versuch in zwei Minuten.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Ein Durchlauf. Oeffentlich, damit ein Test ihn ohne Warten ausloesen kann.</summary>
    public async Task<int> DurchlaufAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopes.CreateScope();
        var mails = scope.ServiceProvider.GetRequiredService<MailService>();

        long? letzte = await mails.LastImapUidAsync(cancellationToken);
        IReadOnlyList<IncomingMail> neu = await mailbox.FetchNewAsync(letzte, cancellationToken);
        return await mails.StoreIncomingAsync(neu, cancellationToken);
    }
}
