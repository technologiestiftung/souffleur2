using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Souffleur.Domain.Modules;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Modules;

/// <summary>
/// Welche Module dieses Haus eingeschaltet hat.
/// </summary>
/// <remarks>
/// Haelt den Stand im Arbeitsspeicher, weil jede Anfrage ihn braucht und er sich
/// fast nie aendert. Wer ihn aendert, ruft <see cref="RefreshAsync"/> -- es gibt
/// nur einen Prozess je Haus, also genuegt das.
/// </remarks>
public class ModuleRegistry(IServiceScopeFactory scopeFactory)
{
    private volatile IReadOnlyDictionary<SouffleurModule, bool> state =
        SouffleurModules.All.ToDictionary(m => m, m => m.IsOnByDefault());

    public bool IsEnabled(SouffleurModule module) =>
        !module.CanBeDisabled() || (state.TryGetValue(module, out bool on) && on);

    public IReadOnlyList<SouffleurModule> Enabled =>
        [.. SouffleurModules.All.Where(IsEnabled)];

    /// <summary>Liest den Stand neu aus der Datenbank.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SouffleurDbContext>();

        List<ModuleSetting> settings = await context.ModuleSettings
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        state = SouffleurModules.All.ToDictionary(
            module => module,
            module => settings.FirstOrDefault(s => s.Module == module)?.IsEnabled
                      ?? module.IsOnByDefault());
    }
}
