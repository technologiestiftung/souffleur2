using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Catalogs;
using Souffleur.Domain.Resources;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Resources;

public class ResourceException(IReadOnlyList<string> problems)
    : Exception(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;

    public ResourceException(string problem) : this([problem])
    {
    }
}

/// <summary>
/// Die Ressourcen des Hauses verwalten -- Bühnen, Räume, Werkstätten, Fahrzeuge.
/// </summary>
/// <remarks>
/// Dieselben Regeln wie bei den Listen des Hauses (ADR-0005): Wer eine
/// Ressource nicht mehr braucht, schaltet sie ab. Löschen geht nur, solange
/// kein Termin sie belegt -- sonst stünde im Spielplan von 2023 eine Bühne
/// ohne Namen.
/// </remarks>
public class ResourceService(SouffleurDbContext context)
{
    public async Task<List<Resource>> ListAsync(
        bool includeInactive = false, CancellationToken token = default) =>
        await context.Resources
            .Where(r => includeInactive || r.IsActive)
            .Include(r => r.Kind)
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.Name)
            .AsNoTracking()
            .ToListAsync(token);

    public async Task<Resource?> FindAsync(Guid id, CancellationToken token = default) =>
        await context.Resources
            .Include(r => r.Kind)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, token);

    public async Task<Resource> AddAsync(Resource resource, CancellationToken token = default)
    {
        await ValidateAsync(resource, token);

        if (resource.SortOrder == 0)
        {
            int highest = await context.Resources.MaxAsync(r => (int?)r.SortOrder, token) ?? 0;
            resource.SortOrder = highest + 10;
        }

        context.Resources.Add(resource);
        await context.SaveChangesAsync(token);
        return resource;
    }

    public async Task<Resource> UpdateAsync(
        Guid id, Action<Resource> apply, CancellationToken token = default)
    {
        Resource found = await TrackedAsync(id, token);
        apply(found);
        await ValidateAsync(found, token);
        await context.SaveChangesAsync(token);
        return found;
    }

    public async Task SetActiveAsync(Guid id, bool active, CancellationToken token = default)
    {
        Resource found = await TrackedAsync(id, token);
        found.IsActive = active;
        await context.SaveChangesAsync(token);
    }

    public async Task DeleteAsync(Guid id, CancellationToken token = default)
    {
        Resource found = await TrackedAsync(id, token);
        int uses = await UsageCountAsync(id, token);

        if (uses > 0)
        {
            throw new ResourceException(
                $"„{found.Name}“ ist {uses}-mal belegt und lässt sich nicht löschen. " +
                "Abschalten nimmt die Ressource aus der Auswahl, lässt sie aber an den " +
                "Terminen lesbar.");
        }

        context.Resources.Remove(found);
        await context.SaveChangesAsync(token);
    }

    /// <summary>Wie viele Termine und Serien die Ressource belegen.</summary>
    public async Task<int> UsageCountAsync(Guid id, CancellationToken token = default) =>
        await context.ResourceBookings.CountAsync(b => b.ResourceId == id, token);

    public async Task ReorderAsync(IReadOnlyList<Guid> order, CancellationToken token = default)
    {
        List<Resource> all = await context.Resources.ToListAsync(token);

        for (int index = 0; index < order.Count; index++)
        {
            Resource? found = all.FirstOrDefault(r => r.Id == order[index]);
            if (found is not null)
            {
                found.SortOrder = (index + 1) * 10;
            }
        }

        await context.SaveChangesAsync(token);
    }

    private async Task<Resource> TrackedAsync(Guid id, CancellationToken token) =>
        await context.Resources.FirstOrDefaultAsync(r => r.Id == id, token)
        ?? throw new ResourceException("Die Ressource gibt es nicht.");

    private async Task ValidateAsync(Resource resource, CancellationToken token)
    {
        List<string> problems = [];

        if (string.IsNullOrWhiteSpace(resource.Name))
        {
            problems.Add("Ein Name ist nötig.");
        }
        else if (await context.Resources.AnyAsync(
                     r => r.Name == resource.Name && r.Id != resource.Id, token))
        {
            problems.Add($"„{resource.Name}“ gibt es schon.");
        }

        if (resource.Capacity is < 0)
        {
            problems.Add("Die Platzzahl kann nicht negativ sein.");
        }

        if (resource.KindId is { } kindId)
        {
            CatalogEntry? kind = await context.CatalogEntries.FirstOrDefaultAsync(
                e => e.Id == kindId && e.Catalog == CatalogKind.ResourceKind, token);

            if (kind is null)
            {
                problems.Add("Die Art gibt es nicht.");
            }
            else if (!kind.IsActive)
            {
                problems.Add($"Die Art „{kind.Label}“ ist abgeschaltet.");
            }
        }

        if (problems.Count > 0)
        {
            throw new ResourceException(problems);
        }
    }
}
