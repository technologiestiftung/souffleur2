using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Api.Infrastructure;

/// <summary>
/// Gesund heisst: Die Datenbank laesst sich lesen.
/// </summary>
/// <remarks>
/// Vorher antwortete <c>/api/health</c> mit 200, solange der Prozess lief --
/// auch wenn die Datenbank fehlte, gesperrt war oder eine Migration auf halbem
/// Weg stehen blieb. Am Gesundheitszustand haengt der Healthcheck des
/// Containers; ein Container, der nichts lesen kann, soll nicht gesund melden.
///
/// Gelesen wird eine echte Tabelle, nicht nur die Verbindung geprueft: SQLite
/// oeffnet auch eine leere oder fremde Datei ohne Fehler.
/// </remarks>
public sealed class DatabaseHealthCheck(SouffleurDbContext context) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext healthContext, CancellationToken cancellationToken = default)
    {
        try
        {
            await context.Organizations.AsNoTracking().AnyAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Der Grund geht ins Protokoll, nicht in die Antwort: /api/health
            // ist ohne Anmeldung erreichbar.
            return HealthCheckResult.Unhealthy("Datenbank nicht lesbar", exception);
        }
    }
}
