using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Calendar;
using Souffleur.Domain.Catalogs;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Catalogs;

public class CatalogException(IReadOnlyList<string> problems)
    : Exception(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;

    public CatalogException(string problem) : this([problem])
    {
    }
}

/// <summary>
/// Die Listen des Hauses verwalten -- siehe ADR-0005.
/// </summary>
public class CatalogService(SouffleurDbContext context)
{
    public IQueryable<CatalogEntry> Query(CatalogKind catalog) =>
        context.CatalogEntries.Where(e => e.Catalog == catalog);

    public async Task<List<CatalogEntry>> ListAsync(
        CatalogKind catalog, bool includeInactive = false, CancellationToken token = default) =>
        await Query(catalog)
            .Where(e => includeInactive || e.IsActive)
            .OrderBy(e => e.SortOrder)
            .ThenBy(e => e.Label)
            .ToListAsync(token);

    public async Task<CatalogEntry> AddAsync(CatalogEntry entry, CancellationToken token = default)
    {
        entry.Key = string.IsNullOrWhiteSpace(entry.Key) ? Slug(entry.Label) : entry.Key.Trim();
        await ValidateAsync(entry, token);

        if (entry.SortOrder == 0)
        {
            // Neues ans Ende, nicht an den Anfang: Die Reihenfolge ist eine
            // Entscheidung des Hauses, und ein neuer Eintrag soll sie nicht
            // ungefragt umwerfen.
            int highest = await Query(entry.Catalog).MaxAsync(e => (int?)e.SortOrder, token) ?? 0;
            entry.SortOrder = highest + 10;
        }

        context.CatalogEntries.Add(entry);
        await context.SaveChangesAsync(token);
        return entry;
    }

    public async Task<CatalogEntry> UpdateAsync(
        Guid id, Action<CatalogEntry> apply, CancellationToken token = default)
    {
        CatalogEntry entry = await FindAsync(id, token);
        string key = entry.Key;

        apply(entry);

        // Der Schlüssel bleibt, was er war. Er ist das, worüber Code und
        // Beispieldaten den Eintrag meinen; ihn zu ändern bräche diese Verweise,
        // ohne dass irgendwo ein Fehler erschiene.
        entry.Key = key;

        await ValidateAsync(entry, token);
        await context.SaveChangesAsync(token);
        return entry;
    }

    public async Task SetActiveAsync(Guid id, bool active, CancellationToken token = default)
    {
        CatalogEntry entry = await FindAsync(id, token);
        entry.IsActive = active;
        await context.SaveChangesAsync(token);
    }

    /// <summary>
    /// Löschen geht nur, solange nichts darauf verweist und der Eintrag nicht
    /// mitgeliefert wurde. Sonst bleibt das Abschalten.
    /// </summary>
    public async Task DeleteAsync(Guid id, CancellationToken token = default)
    {
        CatalogEntry entry = await FindAsync(id, token);

        if (entry.IsSystem)
        {
            throw new CatalogException(
                $"„{entry.Label}“ wird mitgeliefert und lässt sich nicht löschen. " +
                "Umbenennen oder abschalten ist möglich.");
        }

        int uses = await UsageCountAsync(entry, token);
        if (uses > 0)
        {
            throw new CatalogException(
                $"„{entry.Label}“ wird {uses}-mal verwendet und lässt sich nicht löschen. " +
                "Abschalten nimmt den Eintrag aus der Auswahl, lässt ihn aber dort lesbar.");
        }

        context.CatalogEntries.Remove(entry);
        await context.SaveChangesAsync(token);
    }

    /// <summary>Die Reihenfolge des Hauses setzen -- die Liste gibt sie vor.</summary>
    public async Task ReorderAsync(
        CatalogKind catalog, IReadOnlyList<Guid> order, CancellationToken token = default)
    {
        List<CatalogEntry> entries = await Query(catalog).ToListAsync(token);

        for (int index = 0; index < order.Count; index++)
        {
            CatalogEntry? entry = entries.FirstOrDefault(e => e.Id == order[index]);
            if (entry is not null)
            {
                entry.SortOrder = (index + 1) * 10;
            }
        }

        await context.SaveChangesAsync(token);
    }

    /// <summary>
    /// Wie oft auf den Eintrag verwiesen wird.
    /// </summary>
    /// <remarks>
    /// Je Liste einzeln aufgezählt und nicht generisch: Kommt eine Verwendung
    /// hinzu, soll der Compiler nicht schweigen können. Die Fremdschlüssel
    /// stehen zusätzlich auf <c>Restrict</c> -- diese Prüfung gibt es für die
    /// Meldung, nicht für die Sicherheit.
    /// </remarks>
    public async Task<int> UsageCountAsync(CatalogEntry entry, CancellationToken token = default)
    {
        int children = await context.CatalogEntries.CountAsync(e => e.ParentId == entry.Id, token);

        int own = entry.Catalog switch
        {
            CatalogKind.EventCategory =>
                await context.CalendarEvents.CountAsync(e => e.CategoryId == entry.Id, token)
                + await context.EventSeries.CountAsync(s => s.CategoryId == entry.Id, token),

            CatalogKind.ResourceKind =>
                await context.Resources.CountAsync(r => r.KindId == entry.Id, token),

            CatalogKind.ParticipationRole =>
                await context.EventParticipants.CountAsync(p => p.FunctionId == entry.Id, token),

            CatalogKind.TaskCategory =>
                await context.TaskItems.CountAsync(t => t.CategoryId == entry.Id, token),

            // Kostenarten und Schlagworte haben noch keine Tabelle, die auf
            // sie verweist. Sobald es sie gibt, gehört sie hierher.
            CatalogKind.CostType => 0,
            CatalogKind.ContactTag => 0,
            _ => 0,
        };

        return children + own;
    }

    public async Task<int> UsageCountAsync(Guid id, CancellationToken token = default) =>
        await UsageCountAsync(await FindAsync(id, token), token);

    private async Task<CatalogEntry> FindAsync(Guid id, CancellationToken token) =>
        await context.CatalogEntries.FirstOrDefaultAsync(e => e.Id == id, token)
        ?? throw new CatalogException("Den Eintrag gibt es nicht.");

    private async Task ValidateAsync(CatalogEntry entry, CancellationToken token)
    {
        List<string> problems = [];

        if (string.IsNullOrWhiteSpace(entry.Label))
        {
            problems.Add("Eine Beschriftung ist nötig.");
        }

        if (string.IsNullOrWhiteSpace(entry.Key))
        {
            problems.Add("Ein Schlüssel ist nötig.");
        }
        else if (await context.CatalogEntries.AnyAsync(
                     e => e.Catalog == entry.Catalog && e.Key == entry.Key && e.Id != entry.Id, token))
        {
            problems.Add($"Den Schlüssel „{entry.Key}“ gibt es in dieser Liste schon.");
        }

        if (entry.Color is { Length: > 0 } color && !IsColor(color))
        {
            problems.Add($"„{color}“ ist keine Farbe in der Form #rrggbb.");
        }

        if (entry.ParentId == entry.Id)
        {
            problems.Add("Ein Eintrag kann sich nicht selbst übergeordnet sein.");
        }

        if (entry.ParentId is not null && !entry.Catalog.AllowsHierarchy())
        {
            problems.Add($"{entry.Catalog.Label()} kennen keine Untergliederung.");
        }

        if (problems.Count > 0)
        {
            throw new CatalogException(problems);
        }
    }

    private static bool IsColor(string value) =>
        value.Length == 7
        && value[0] == '#'
        && value.Skip(1).All(Uri.IsHexDigit);

    /// <summary>
    /// Aus der Beschriftung einen Schlüssel machen, wenn keiner angegeben wurde.
    /// </summary>
    private static string Slug(string label)
    {
        string lowered = label.Trim().ToLowerInvariant()
            .Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss");

        var result = new System.Text.StringBuilder(lowered.Length);
        foreach (char character in lowered)
        {
            result.Append(char.IsAsciiLetterOrDigit(character) ? character : '-');
        }

        string slug = result.ToString().Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Length == 0 ? Guid.CreateVersion7().ToString("n")[..8] : slug;
    }
}
