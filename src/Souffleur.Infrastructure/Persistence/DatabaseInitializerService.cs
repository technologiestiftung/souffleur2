using Microsoft.Extensions.Hosting;

namespace Souffleur.Infrastructure.Persistence;

/// <summary>
/// Bringt die Datenbank beim Start auf Stand.
/// </summary>
/// <remarks>
/// Als Hosted Service und nicht als Aufruf in <c>Program.cs</c>: Die
/// Testinfrastruktur (<c>WebApplicationFactory</c>) fuehrt den Startcode einmal
/// aus, bevor sie ihre eigene Konfiguration unterschiebt. Ein direkter Aufruf
/// liefe dabei gegen die Standarddatenbank statt gegen die des Tests -- bei
/// mehreren Testklassen gleichzeitig gegen dieselbe Datei.
/// </remarks>
public class DatabaseInitializerService(IServiceProvider services) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        DatabaseInitializer.InitializeAsync(services, cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
