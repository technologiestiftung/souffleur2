using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Modules;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Modules;

/// <summary>
/// Schaltet Module an und aus. Einziger Weg dorthin, damit der Stand im
/// Arbeitsspeicher nicht hinter der Datenbank zurueckbleibt.
/// </summary>
public class ModuleService(SouffleurDbContext context, ModuleRegistry registry)
{
    public async Task<IReadOnlyList<ModuleSetting>> ListAsync(CancellationToken cancellationToken = default)
    {
        List<ModuleSetting> stored = await context.ModuleSettings.ToListAsync(cancellationToken);

        // Module, die es in der Datenbank noch nicht gibt -- etwa nach einer
        // Aktualisierung, die ein neues mitbringt -- erscheinen mit ihrer Vorgabe.
        return
        [
            .. SouffleurModules.All.Select(module =>
                stored.FirstOrDefault(s => s.Module == module)
                ?? new ModuleSetting { Module = module, IsEnabled = module.IsOnByDefault() })
        ];
    }

    public async Task SetAsync(
        SouffleurModule module,
        bool enabled,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        if (!enabled && !module.CanBeDisabled())
        {
            throw new InvalidOperationException($"{module.Label()} lässt sich nicht abschalten.");
        }

        ModuleSetting? setting = await context.ModuleSettings
            .FirstOrDefaultAsync(s => s.Module == module, cancellationToken);

        if (setting is null)
        {
            setting = new ModuleSetting { Module = module, IsEnabled = enabled, Note = note };
            context.ModuleSettings.Add(setting);
        }
        else if (enabled)
        {
            setting.Enable();
        }
        else
        {
            setting.Disable(note);
        }

        await context.SaveChangesAsync(cancellationToken);
        await registry.RefreshAsync(cancellationToken);
    }
}
