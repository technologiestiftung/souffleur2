using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Souffleur.Infrastructure.Documents;

/// <summary>
/// Liest die Ordner der Ablage im Takt ein -- damit erscheint in souffleur,
/// was jemand in der File Station ablegt, ohne dass jemand auf einen Knopf
/// druecken muss.
/// </summary>
/// <remarks>
/// Fuenf Minuten, weil eine Synology mit zwei Gigabyte Speicher keine
/// Dauerbeschaeftigung braucht: Ein Durchlauf liest nur Groesse und
/// Aenderungszeit und rechnet den Pruefwert nur fuer Neues und Geaendertes.
/// Wer es eilig hat, liest in den Einstellungen von Hand ein.
///
/// Der erste Durchlauf wartet, bis die Migrationen durch sind -- dasselbe wie
/// beim Postfach.
/// </remarks>
public class FolderScanner(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<FolderScanner> logger) : BackgroundService
{
    private static readonly TimeSpan Anlauf = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int minuten = configuration.GetValue("Souffleur:Ablage:TaktMinuten", 5);
        if (minuten <= 0)
        {
            logger.LogInformation("Ablage: Einlesen im Takt ist abgeschaltet.");
            return;
        }

        await Task.Delay(Anlauf, time, stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minuten), time);
        do
        {
            try
            {
                using IServiceScope scope = scopes.CreateScope();
                var storage = scope.ServiceProvider.GetRequiredService<FolderStorage>();
                if (!storage.IsAvailable)
                {
                    continue;
                }

                var folders = scope.ServiceProvider.GetRequiredService<FolderService>();
                foreach ((Guid id, string ergebnis) in await folders.ScanAllAsync(stoppingToken))
                {
                    if (ergebnis != "nichts Neues")
                    {
                        logger.LogInformation("Ablage {Ordner}: {Ergebnis}.", id, ergebnis);
                    }
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Ablage: Einlesen gescheitert, nächster Versuch im nächsten Takt.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
