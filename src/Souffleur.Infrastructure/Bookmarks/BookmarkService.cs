using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Bookmarks;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Bookmarks;

/// <summary>
/// Lesezeichen an Gegenstaenden des Hauses.
/// </summary>
/// <remarks>
/// Der Dienst prueft nicht, ob der Gegenstand existiert: Er kennt die
/// Module nicht, an die er sich heftet, und soll sie nicht kennen muessen --
/// sonst waere jedes neue Modul hier eine Zeile. Ein Lesezeichen an einem
/// Gegenstand, den es nicht mehr gibt, hat niemand vor Augen und stoert
/// nicht. Wer einen Gegenstand loescht, raeumt ueber
/// <see cref="RemoveAllAsync"/> hinterher.
/// </remarks>
public class BookmarkService(SouffleurDbContext context)
{
    public Task<List<Bookmark>> ListAsync(
        BookmarkTarget target, Guid targetId, CancellationToken cancellationToken = default) =>
        context.Bookmarks
            .Where(b => b.Target == target && b.TargetId == targetId)
            .OrderBy(b => b.SortOrder).ThenBy(b => b.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    /// <summary>Wie viele an welchem Gegenstand haengen -- fuer die Zahl in einer Liste.</summary>
    public async Task<IReadOnlyDictionary<Guid, int>> CountAsync(
        BookmarkTarget target, IReadOnlyCollection<Guid> targetIds, CancellationToken cancellationToken = default)
    {
        var rows = await context.Bookmarks
            .Where(b => b.Target == target && targetIds.Contains(b.TargetId))
            .GroupBy(b => b.TargetId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.Key, r => r.Count);
    }

    public async Task<Bookmark> AddAsync(
        BookmarkTarget target,
        Guid targetId,
        string url,
        string? label,
        CancellationToken cancellationToken = default)
    {
        int next = await context.Bookmarks
            .Where(b => b.Target == target && b.TargetId == targetId)
            .Select(b => (int?)b.SortOrder)
            .MaxAsync(cancellationToken) ?? -1;

        var bookmark = new Bookmark
        {
            Target = target,
            TargetId = targetId,
            Url = Bookmark.Normalize(url),
            Label = Clean(label),
            SortOrder = next + 1,
        };

        Reject(bookmark);
        context.Bookmarks.Add(bookmark);
        await context.SaveChangesAsync(cancellationToken);
        return bookmark;
    }

    public async Task<Bookmark> UpdateAsync(
        Guid id, string url, string? label, CancellationToken cancellationToken = default)
    {
        Bookmark bookmark = await context.Bookmarks.FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
            ?? throw new BookmarkException(["Dieses Lesezeichen gibt es nicht."]);

        bookmark.Url = Bookmark.Normalize(url);
        bookmark.Label = Clean(label);

        Reject(bookmark);
        await context.SaveChangesAsync(cancellationToken);
        return bookmark;
    }

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Bookmark? bookmark = await context.Bookmarks.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (bookmark is null)
        {
            return;
        }

        context.Bookmarks.Remove(bookmark);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Wenn der Gegenstand selbst verschwindet.</summary>
    public Task<int> RemoveAllAsync(
        BookmarkTarget target, Guid targetId, CancellationToken cancellationToken = default) =>
        context.Bookmarks
            .Where(b => b.Target == target && b.TargetId == targetId)
            .ExecuteDeleteAsync(cancellationToken);

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Reject(Bookmark bookmark)
    {
        IReadOnlyList<string> problems = bookmark.Validate();
        if (problems.Count > 0)
        {
            throw new BookmarkException(problems);
        }
    }
}

public class BookmarkException(IReadOnlyList<string> problems)
    : InvalidOperationException(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}
