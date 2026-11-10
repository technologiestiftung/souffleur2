using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Documents;
using Souffleur.Domain.Modules;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Documents;

/// <summary>Etwas am Dokument passt nicht -- mit den Gruenden in Worten.</summary>
public class DocumentException(IReadOnlyList<string> problems)
    : Exception(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>
/// Wer gerade fragt. Ob jemand fremde Dokumente verwalten darf, steht in
/// seiner Sitzung und wird von der Schnittstelle mitgegeben.
/// </summary>
public record DocumentViewer(Guid? UserId, bool CanManage);

/// <summary>
/// Wohin eine neue Datei soll. Ohne Angabe entscheidet das Modul, aus dem sie
/// kommt (ADR-0013); <see cref="Internal"/> haelt sie ausdruecklich in
/// souffleurs eigener Ablage.
/// </summary>
public sealed record FolderChoice(Guid? FolderId = null, bool Internal = false)
{
    public static readonly FolderChoice Automatic = new();
}

/// <summary>Was beim Ablegen und Aendern mitkommt.</summary>
public record DocumentInput(
    string? Name,
    string? Note,
    IReadOnlyCollection<string> Tags,
    IReadOnlyCollection<Guid> GroupIds,
    bool Restricted);

/// <summary>Die Ablage des Hauses.</summary>
/// <remarks>
/// Wer was sieht, steht an genau einer Stelle -- <see cref="Visible"/>. Jede
/// Abfrage geht dort hindurch; ein Weg daran vorbei waere ein Weg zu den
/// Personalakten.
/// </remarks>
public class DocumentService(
    SouffleurDbContext context,
    DocumentStorage storage,
    FolderStorage folders,
    FolderService folderService)
{
    private const int MaxTags = 20;
    private const int MaxTagLength = 50;

    /// <summary>
    /// Was diese Person sehen darf: alles Offene, was sie selbst abgelegt
    /// hat, was an einer ihrer Gruppen haengt -- oder alles, wenn sie fremde
    /// Dokumente verwalten darf.
    /// </summary>
    public IQueryable<Document> Visible(DocumentViewer viewer)
    {
        IQueryable<Document> alle = context.Documents;
        if (viewer.CanManage)
        {
            return alle;
        }

        Guid userId = viewer.UserId ?? Guid.Empty;
        IQueryable<Guid> meineGruppen = context.GroupMembers
            .Where(m => m.UserId == userId)
            .Join(context.Groups.Where(g => g.IsActive), m => m.GroupId, g => g.Id, (_, g) => g.Id);

        return alle.Where(d =>
            !d.IsRestricted
            || d.CreatedBy == userId
            || d.Groups.Any(g => meineGruppen.Contains(g.GroupId)));
    }

    public async Task<List<Document>> ListAsync(
        DocumentViewer viewer,
        string? search = null,
        string? tag = null,
        DocumentTarget? target = null,
        Guid? targetId = null,
        Guid? folderId = null,
        CancellationToken token = default)
    {
        IQueryable<Document> query = Visible(viewer)
            .Include(d => d.Folder)
            .Include(d => d.Tags)
            .Include(d => d.Links)
            .Include(d => d.Groups).ThenInclude(g => g.Group)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            string s = search.Trim().ToLower();
            query = query.Where(d =>
                d.Name.ToLower().Contains(s)
                || d.OriginalName.ToLower().Contains(s)
                || (d.Note != null && d.Note.ToLower().Contains(s))
                || d.Tags.Any(t => t.Tag.Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            string t = DocumentTag.Normalize(tag);
            query = query.Where(d => d.Tags.Any(x => x.Tag == t));
        }

        if (target is { } art && targetId is { } id)
        {
            query = query.Where(d => d.Links.Any(l => l.Target == art && l.TargetId == id));
        }

        if (folderId is { } ordner)
        {
            query = query.Where(d => d.FolderId == ordner);
        }

        return await query
            .OrderByDescending(d => d.CreatedAt)
            .Take(500)
            .ToListAsync(token);
    }

    public async Task<Document?> FindAsync(Guid id, DocumentViewer viewer, CancellationToken token = default) =>
        await Visible(viewer)
            .Include(d => d.Folder)
            .Include(d => d.Tags)
            .Include(d => d.Links)
            .Include(d => d.Groups).ThenInclude(g => g.Group)
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, token);

    /// <summary>Die Schlagworte, die es schon gibt -- zum Vorschlagen.</summary>
    public async Task<List<string>> TagsAsync(DocumentViewer viewer, CancellationToken token = default) =>
        await Visible(viewer)
            .SelectMany(d => d.Tags)
            .Select(t => t.Tag)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync(token);

    /// <summary>Gleich aussehende Dateien, die schon in der Ablage liegen -- als Hinweis, nicht als Sperre.</summary>
    public async Task<List<string>> DuplicatesAsync(
        Guid id, string sha256, DocumentViewer viewer, CancellationToken token = default) =>
        await Visible(viewer)
            .Where(d => d.Sha256 == sha256 && d.Id != id)
            .Select(d => d.Name)
            .ToListAsync(token);

    public async Task<Document> UploadAsync(
        Stream content,
        string? originalName,
        DocumentInput input,
        IReadOnlyCollection<(DocumentTarget Target, Guid TargetId)> links,
        CancellationToken token = default) =>
        await UploadAsync(content, originalName, input, links, FolderChoice.Automatic, token);

    public async Task<Document> UploadAsync(
        Stream content,
        string? originalName,
        DocumentInput input,
        IReadOnlyCollection<(DocumentTarget Target, Guid TargetId)> links,
        FolderChoice choice,
        CancellationToken token = default)
    {
        List<string> problems = await PruefenAsync(input, token);
        foreach ((DocumentTarget art, Guid id) in links)
        {
            if (!await ExistsAsync(art, id, token))
            {
                problems.Add($"{Label(art)} gibt es nicht.");
            }
        }

        if (problems.Count > 0)
        {
            throw new DocumentException(problems);
        }

        var document = new Document
        {
            Name = string.Empty,
            OriginalName = string.Empty,
            ContentType = string.Empty,
            SizeInBytes = 0,
            StoredAs = string.Empty,
            Sha256 = string.Empty,
        };

        DocumentFolder? folder = await OrdnerFuerAsync(choice, links, token);
        Action aufraeumen;

        if (folder is null)
        {
            DocumentStorage.Stored stored = await storage.StoreAsync(document.Id, content, originalName, token);

            document.OriginalName = stored.OriginalName;
            document.ContentType = stored.ContentType;
            document.SizeInBytes = stored.Size;
            document.StoredAs = stored.StoredAs;
            document.Sha256 = stored.Sha256;
            aufraeumen = () => storage.Delete(stored.StoredAs);
        }
        else
        {
            // Dieselbe Pruefung der Art wie in der eigenen Ablage: Was die
            // File Station von sich aus hineinlegt, ist Sache des Hauses --
            // was ueber souffleur hineinkommt, nicht.
            if (DocumentStorage.Classify(originalName) is not { } art)
            {
                throw new DocumentFileException(
                    $"Dateien wie „{FolderStorage.SafeSegment(originalName)}“ gehören nicht in die Ablage.");
            }

            string? unterordner = await UnterordnerAsync(links, token);
            string pfad = await folders.WriteAsync(
                folder.Path, unterordner, originalName ?? "Datei", content, token, DocumentStorage.MaxSizeInBytes);
            FileInfo info = folders.Info(folder.Path, pfad);

            document.OriginalName = pfad.Split('/')[^1];
            document.ContentType = art.ContentType;
            document.SizeInBytes = info.Length;
            document.Sha256 = await folders.HashAsync(folder.Path, pfad, token);
            document.FolderId = folder.Id;
            document.FolderPath = pfad;
            document.FileModifiedAt = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
            aufraeumen = () => folders.Delete(folder.Path, pfad);

            // Ohne eigene Wahl gilt die Sichtbarkeit des Ordners.
            if (folder.IsRestricted && !input.Restricted)
            {
                input = input with { Restricted = true, GroupIds = [.. folder.Groups.Select(g => g.GroupId)] };
            }
        }

        Anwenden(document, input, fallbackName: Path.GetFileNameWithoutExtension(document.OriginalName));

        foreach ((DocumentTarget art, Guid id) in links.Distinct())
        {
            document.Links.Add(new DocumentLink { DocumentId = document.Id, Target = art, TargetId = id });
        }

        try
        {
            context.Documents.Add(document);
            await context.SaveChangesAsync(token);
        }
        catch
        {
            // Ohne Zeile in der Datenbank ist die Datei verwaist -- weg damit.
            aufraeumen();
            throw;
        }

        return document;
    }

    /// <summary>
    /// Name, Notiz, Schlagworte und Sichtbarkeit aendern. Eigene Dokumente
    /// darf aendern, wer ablegen darf; fremde nur, wer sie verwalten darf.
    /// </summary>
    public async Task<bool> UpdateAsync(
        Guid id, DocumentInput input, DocumentViewer viewer, CancellationToken token = default)
    {
        Document? document = await Bearbeitbar(id, viewer, token);
        if (document is null)
        {
            return false;
        }

        List<string> problems = await PruefenAsync(input, token);
        if (problems.Count > 0)
        {
            throw new DocumentException(problems);
        }

        // Erst raeumen, dann fuellen -- dieselbe Kombination laege sonst
        // kurz zweimal auf einem eindeutigen Index.
        await context.DocumentTags.Where(t => t.DocumentId == id).ExecuteDeleteAsync(token);
        await context.DocumentGroups.Where(g => g.DocumentId == id).ExecuteDeleteAsync(token);
        document.Tags.Clear();
        document.Groups.Clear();

        Anwenden(document, input, fallbackName: document.Name);

        // Ausdruecklich hinzufuegen: Entity vergibt die Id im Konstruktor,
        // und ein Datensatz mit gesetztem Schluessel, der nur an der Liste
        // eines geladenen Dokuments haengt, gilt EF als vorhanden -- daraus
        // wuerde ein UPDATE auf eine Zeile, die es nicht gibt.
        context.DocumentTags.AddRange(document.Tags);
        context.DocumentGroups.AddRange(document.Groups);

        await context.SaveChangesAsync(token);
        return true;
    }

    public async Task<bool> LinkAsync(
        Guid id, DocumentTarget target, Guid targetId, DocumentViewer viewer, CancellationToken token = default)
    {
        if (await Visible(viewer).AnyAsync(d => d.Id == id, token) is false)
        {
            return false;
        }

        if (!await ExistsAsync(target, targetId, token))
        {
            throw new DocumentException([$"{Label(target)} gibt es nicht."]);
        }

        bool schonDa = await context.DocumentLinks.AnyAsync(
            l => l.DocumentId == id && l.Target == target && l.TargetId == targetId, token);
        if (!schonDa)
        {
            context.DocumentLinks.Add(new DocumentLink { DocumentId = id, Target = target, TargetId = targetId });
            await context.SaveChangesAsync(token);
        }

        return true;
    }

    public async Task<bool> UnlinkAsync(Guid id, Guid linkId, DocumentViewer viewer, CancellationToken token = default)
    {
        if (await Visible(viewer).AnyAsync(d => d.Id == id, token) is false)
        {
            return false;
        }

        await context.DocumentLinks
            .Where(l => l.Id == linkId && l.DocumentId == id)
            .ExecuteDeleteAsync(token);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, DocumentViewer viewer, CancellationToken token = default)
    {
        Document? document = await Bearbeitbar(id, viewer, token);
        if (document is null)
        {
            return false;
        }

        DocumentFolder? folder = document.FolderId is { } folderId
            ? await context.DocumentFolders.AsNoTracking().FirstOrDefaultAsync(f => f.Id == folderId, token)
            : null;

        context.Documents.Remove(document);
        await context.SaveChangesAsync(token);

        // Die Datei erst nach der Zeile: Scheitert das Speichern, ist sonst
        // ein Dokument da, dessen Datei fehlt. Im Ordner geht sie in den
        // Papierkorb der Freigabe, nicht ins Nichts.
        if (folder is not null && document.FolderPath is { } pfad)
        {
            folders.Delete(folder.Path, pfad);
        }
        else
        {
            storage.Delete(document.StoredAs);
        }

        return true;
    }

    /// <summary>Braucht den Ordner am Dokument -- <see cref="FindAsync"/> laedt ihn mit.</summary>
    public Stream OpenRead(Document document) =>
        document is { Folder: { } folder, FolderPath: { } pfad }
            ? folders.OpenRead(folder.Path, pfad)
            : storage.OpenRead(document.StoredAs);

    public bool FileExists(Document document) =>
        document is { Folder: { } folder, FolderPath: { } pfad }
            ? folders.Exists(folder.Path, pfad)
            : document.FolderId is null && storage.Exists(document.StoredAs);

    /// <summary>Wie ein verknuepfter Gegenstand heisst -- fuer die Anzeige am Dokument.</summary>
    public async Task<Dictionary<(DocumentTarget, Guid), string>> LabelsAsync(
        IEnumerable<DocumentLink> links, CancellationToken token = default)
    {
        var namen = new Dictionary<(DocumentTarget, Guid), string>();
        foreach (IGrouping<DocumentTarget, DocumentLink> art in links.GroupBy(l => l.Target))
        {
            List<Guid> ids = [.. art.Select(l => l.TargetId).Distinct()];
            IEnumerable<(Guid Id, string Name)> gefunden = art.Key switch
            {
                DocumentTarget.Contact => (await context.Contacts.Where(c => ids.Contains(c.Id))
                    .Select(c => new { c.Id, c.Name }).ToListAsync(token)).Select(x => (x.Id, x.Name)),
                DocumentTarget.Task => (await context.TaskItems.Where(t => ids.Contains(t.Id))
                    .Select(t => new { t.Id, t.Title }).ToListAsync(token)).Select(x => (x.Id, x.Title)),
                DocumentTarget.Event => (await context.CalendarEvents.Where(e => ids.Contains(e.Id))
                    .Select(e => new { e.Id, e.Title }).ToListAsync(token)).Select(x => (x.Id, x.Title)),
                DocumentTarget.JournalEntry => (await context.JournalEntries.Where(j => ids.Contains(j.Id))
                    .Select(j => new { j.Id, j.Description }).ToListAsync(token)).Select(x => (x.Id, x.Description)),
                DocumentTarget.Voucher => (await context.Vouchers.Where(v => ids.Contains(v.Id))
                    .ToListAsync(token)).Select(v => (v.Id, v.Label)),
                DocumentTarget.TourStop => (await context.TourStops.Where(s => ids.Contains(s.Id))
                    .Include(s => s.Tour).Include(s => s.Venue)
                    .ToListAsync(token)).Select(s => (s.Id, s.DisplayTitle(s.Tour.Title))),
                DocumentTarget.Production => (await context.Productions.Where(p => ids.Contains(p.Id))
                    .Select(p => new { p.Id, p.Title }).ToListAsync(token)).Select(x => (x.Id, x.Title)),
                _ => [],
            };

            foreach ((Guid id, string name) in gefunden)
            {
                namen[(art.Key, id)] = name;
            }
        }

        return namen;
    }

    public static string Label(DocumentTarget target) => target switch
    {
        DocumentTarget.Contact => "Den Kontakt",
        DocumentTarget.Task => "Die Aufgabe",
        DocumentTarget.Event => "Den Termin",
        DocumentTarget.JournalEntry => "Die Buchung",
        DocumentTarget.Voucher => "Den Beleg",
        DocumentTarget.TourStop => "Das Gastspiel",
        DocumentTarget.Production => "Die Produktion",
        _ => "Das Ziel",
    };

    // ── intern ───────────────────────────────────────────────────────────

    private async Task<DocumentFolder?> OrdnerFuerAsync(
        FolderChoice choice,
        IReadOnlyCollection<(DocumentTarget Target, Guid TargetId)> links,
        CancellationToken token)
    {
        if (choice.Internal)
        {
            return null;
        }

        if (choice.FolderId is { } id)
        {
            return await folderService.FindAsync(id, token)
                   ?? throw new DocumentException(["Den gewählten Ordner gibt es nicht mehr."]);
        }

        // Das erste Ziel entscheidet: Ein Textbuch, das an der Produktion
        // und am Kontakt der Autorin haengt, gehoert zur Produktion -- so
        // wird es auch hochgeladen.
        DocumentFolder? folder = await folderService.DefaultForAsync(
            links.Count > 0 ? FolderService.ModuleOf(links.First().Target) : SouffleurModule.Documents, token);

        // Erst die Datenbank fragen, dann das Dateisystem: Ein Haus ohne
        // Ordner soll beim Hochladen nichts auf der Platte anlegen.
        return folder is not null && folders.FolderExists(folder.Path) ? folder : null;
    }

    /// <summary>
    /// Ein Unterordner je Gegenstand -- „Produktionen/Hamlet/" statt alles
    /// nebeneinander. So findet das Haus in der File Station wieder, was es in
    /// souffleur an die Produktion gehaengt hat.
    /// </summary>
    private async Task<string?> UnterordnerAsync(
        IReadOnlyCollection<(DocumentTarget Target, Guid TargetId)> links,
        CancellationToken token)
    {
        if (links.Count == 0)
        {
            return null;
        }

        (DocumentTarget art, Guid id) = links.First();
        if (art is not (DocumentTarget.Production or DocumentTarget.TourStop or DocumentTarget.Task
            or DocumentTarget.Contact or DocumentTarget.Event))
        {
            return null;
        }

        Dictionary<(DocumentTarget, Guid), string> namen = await LabelsAsync(
            [new DocumentLink { DocumentId = Guid.Empty, Target = art, TargetId = id }], token);

        return namen.TryGetValue((art, id), out string? name) ? FolderStorage.SafeSegment(name) : null;
    }

    private async Task<Document?> Bearbeitbar(Guid id, DocumentViewer viewer, CancellationToken token)
    {
        Document? document = await Visible(viewer).FirstOrDefaultAsync(d => d.Id == id, token);
        if (document is null)
        {
            return null;
        }

        // Sehen heisst nicht aendern: Fremdes nur mit dem Verwaltungsrecht.
        return viewer.CanManage || document.CreatedBy == viewer.UserId ? document : null;
    }

    private async Task<List<string>> PruefenAsync(DocumentInput input, CancellationToken token)
    {
        var problems = new List<string>();

        if (input.Name is { Length: > 200 })
        {
            problems.Add("Der Name ist länger als 200 Zeichen.");
        }

        List<string> tags = [.. input.Tags.Select(DocumentTag.Normalize).Where(t => t.Length > 0).Distinct()];
        if (tags.Count > MaxTags)
        {
            problems.Add($"Höchstens {MaxTags} Schlagworte.");
        }

        if (tags.Any(t => t.Length > MaxTagLength))
        {
            problems.Add($"Ein Schlagwort hat höchstens {MaxTagLength} Zeichen.");
        }

        if (input.Restricted && input.GroupIds.Count == 0)
        {
            problems.Add("Wer soll es sehen? Eingeschränkt heißt: mindestens eine Gruppe.");
        }

        List<Guid> gruppen = [.. input.GroupIds.Distinct()];
        if (gruppen.Count > 0)
        {
            int vorhanden = await context.Groups.CountAsync(g => gruppen.Contains(g.Id) && g.IsActive, token);
            if (vorhanden != gruppen.Count)
            {
                problems.Add("Eine der Gruppen gibt es nicht, oder sie ist stillgelegt.");
            }
        }

        return problems;
    }

    private static void Anwenden(Document document, DocumentInput input, string fallbackName)
    {
        document.Name = string.IsNullOrWhiteSpace(input.Name) ? fallbackName : input.Name.Trim();
        document.Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        document.IsRestricted = input.Restricted;

        foreach (string tag in input.Tags.Select(DocumentTag.Normalize).Where(t => t.Length > 0).Distinct())
        {
            document.Tags.Add(new DocumentTag { DocumentId = document.Id, Tag = tag });
        }

        if (input.Restricted)
        {
            foreach (Guid groupId in input.GroupIds.Distinct())
            {
                document.Groups.Add(new DocumentGroup { DocumentId = document.Id, GroupId = groupId });
            }
        }
    }

    private async Task<bool> ExistsAsync(DocumentTarget target, Guid id, CancellationToken token) => target switch
    {
        DocumentTarget.Contact => await context.Contacts.AnyAsync(c => c.Id == id, token),
        DocumentTarget.Task => await context.TaskItems.AnyAsync(t => t.Id == id, token),
        DocumentTarget.Event => await context.CalendarEvents.AnyAsync(e => e.Id == id, token),
        DocumentTarget.JournalEntry => await context.JournalEntries.AnyAsync(j => j.Id == id, token),
        DocumentTarget.Voucher => await context.Vouchers.AnyAsync(v => v.Id == id, token),
        DocumentTarget.TourStop => await context.TourStops.AnyAsync(s => s.Id == id, token),
        DocumentTarget.Production => await context.Productions.AnyAsync(p => p.Id == id, token),
        _ => false,
    };
}
