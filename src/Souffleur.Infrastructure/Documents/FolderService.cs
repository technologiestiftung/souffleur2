using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Souffleur.Domain.Accounting;
using Souffleur.Domain.Documents;
using Souffleur.Domain.Modules;
using Souffleur.Infrastructure.Accounting;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Documents;

/// <summary>Was ein Ordner beim Anlegen und Aendern mitbringt.</summary>
public sealed record FolderInput(
    string Name,
    string Path,
    SouffleurModule Module,
    bool IsDefault,
    bool IsRestricted,
    IReadOnlyCollection<Guid> GroupIds);

/// <summary>Was ein Durchlauf gefunden hat.</summary>
public sealed record ScanResult(int Added, int Moved, int Changed, int Missing, int Returned)
{
    public override string ToString()
    {
        List<string> teile = [];
        if (Added > 0) teile.Add($"{Added} neu");
        if (Moved > 0) teile.Add($"{Moved} umbenannt oder verschoben");
        if (Changed > 0) teile.Add($"{Changed} geändert");
        if (Returned > 0) teile.Add($"{Returned} wieder da");
        if (Missing > 0) teile.Add($"{Missing} fehlen");
        return teile.Count == 0 ? "nichts Neues" : string.Join(", ", teile);
    }
}

/// <summary>
/// Die Ordner der Ablage: welche es gibt, welches Modul wohin ablegt, und was
/// darin liegt (ADR-0013).
/// </summary>
/// <remarks>
/// Das Einlesen gleicht den Ordner mit der Datenbank ab. Der Pfad ist die
/// erste Frage, der Pruefwert die zweite: Eine Datei, deren Pfad souffleur
/// nicht kennt, deren Inhalt aber zu einem Dokument passt, dessen Datei
/// fehlt, ist umbenannt oder verschoben worden -- und behaelt Schlagworte und
/// Verknuepfungen. Erst was auch so nicht passt, ist neu.
/// </remarks>
public class FolderService(
    SouffleurDbContext context,
    FolderStorage storage,
    VoucherStorage vouchers,
    TimeProvider time,
    ILogger<FolderService> logger)
{
    /// <summary>
    /// Ein Durchlauf zur Zeit, egal ob vom Takt oder vom Knopf. Zwei
    /// gleichzeitig legten dieselbe neue Datei zweimal an.
    /// </summary>
    private static readonly SemaphoreSlim Einer = new(1, 1);

    /// <summary>
    /// Juenger als das wird eine Datei beim Einlesen uebergangen: Sie wird
    /// vielleicht gerade noch geschrieben -- per SMB oder von souffleur selbst,
    /// dessen Dokument erst Augenblicke spaeter in der Datenbank steht.
    /// </summary>
    private static readonly TimeSpan Ruhezeit = TimeSpan.FromSeconds(15);

    public Task<List<DocumentFolder>> ListAsync(CancellationToken token = default) =>
        context.DocumentFolders
            .Include(f => f.Groups).ThenInclude(g => g.Group)
            .OrderBy(f => f.Module).ThenByDescending(f => f.IsDefault).ThenBy(f => f.Name)
            .AsNoTracking()
            .ToListAsync(token);

    /// <summary>Wie viele Dokumente in jedem Ordner stehen, und wie viele davon fehlen.</summary>
    public async Task<Dictionary<Guid, (int Files, int Missing)>> CountsAsync(CancellationToken token = default) =>
        (await context.Documents
            .Where(d => d.FolderId != null)
            .GroupBy(d => d.FolderId!.Value)
            .Select(g => new { g.Key, Files = g.Count(), Missing = g.Count(d => d.MissingSince != null) })
            .ToListAsync(token))
        .ToDictionary(x => x.Key, x => (x.Files, x.Missing));

    public async Task<DocumentFolder> SaveAsync(Guid? id, FolderInput input, CancellationToken token = default)
    {
        DocumentFolder folder;
        if (id is { } existing)
        {
            folder = await context.DocumentFolders.Include(f => f.Groups).FirstOrDefaultAsync(f => f.Id == existing, token)
                     ?? throw new DocumentException(["Den Ordner gibt es nicht mehr."]);
        }
        else
        {
            folder = new DocumentFolder { Name = string.Empty, Path = string.Empty, Module = input.Module };
            context.DocumentFolders.Add(folder);
        }

        folder.Name = input.Name.Trim();
        folder.Path = DocumentFolder.NormalizePath(input.Path);
        folder.Module = input.Module;
        folder.IsDefault = input.IsDefault;
        folder.IsRestricted = input.IsRestricted;

        List<string> problems = [.. folder.Validate()];

        if (problems.Count == 0 && !storage.FolderExists(folder.Path))
        {
            problems.Add($"Den Ordner „{folder.Path}“ gibt es auf der Synology nicht.");
        }

        if (await context.DocumentFolders.AnyAsync(f => f.Path == folder.Path && f.Id != folder.Id, token))
        {
            problems.Add("Dieser Ordner ist schon eingetragen.");
        }

        // Ein Ordner im anderen hiesse: Dieselbe Datei gehoerte zweien, und
        // jeder Durchlauf schoebe sie hin und her.
        List<string> andere = await context.DocumentFolders
            .Where(f => f.Id != folder.Id)
            .Select(f => f.Path)
            .ToListAsync(token);
        if (andere.FirstOrDefault(p => p.StartsWith(folder.Path + "/", StringComparison.Ordinal)
                                       || folder.Path.StartsWith(p + "/", StringComparison.Ordinal)) is { } verschachtelt)
        {
            problems.Add($"„{folder.Path}“ und „{verschachtelt}“ liegen ineinander. Ein Ordner darf nur einmal vorkommen.");
        }

        if (input.IsRestricted && input.GroupIds.Count == 0)
        {
            problems.Add("Wer soll es sehen? Eingeschränkt heißt: mindestens eine Gruppe.");
        }

        if (problems.Count > 0)
        {
            throw new DocumentException(problems);
        }

        // Je Modul ein Standard: Wer einen neuen setzt, nimmt ihn dem alten.
        if (folder.IsDefault)
        {
            await context.DocumentFolders
                .Where(f => f.Module == folder.Module && f.Id != folder.Id && f.IsDefault)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.IsDefault, false), token);
        }

        await context.DocumentFolderGroups.Where(g => g.FolderId == folder.Id).ExecuteDeleteAsync(token);
        folder.Groups.Clear();
        if (input.IsRestricted)
        {
            // Ausdruecklich hinzufuegen -- an einem geladenen Ordner hielte EF
            // die neue Zeile sonst fuer vorhanden.
            context.DocumentFolderGroups.AddRange(input.GroupIds.Distinct().Select(g =>
                new DocumentFolderGroup { FolderId = folder.Id, GroupId = g }));
        }

        await context.SaveChangesAsync(token);
        return folder;
    }

    /// <summary>
    /// Den Ordner austragen. Die Dateien bleiben auf der Synology; souffleur
    /// vergisst nur, was es ueber sie wusste.
    /// </summary>
    public async Task RemoveAsync(Guid id, CancellationToken token = default)
    {
        DocumentFolder? folder = await context.DocumentFolders.FirstOrDefaultAsync(f => f.Id == id, token);
        if (folder is not null)
        {
            context.DocumentFolders.Remove(folder);
            await context.SaveChangesAsync(token);
        }
    }

    /// <summary>
    /// Wohin ein Dokument gehoert, das aus einem Modul heraus hochgeladen wird:
    /// der Standardordner des Moduls, sonst der des Dokumentenmoduls, sonst
    /// keiner -- dann bleibt es in souffleurs eigener Ablage.
    /// </summary>
    public async Task<DocumentFolder?> DefaultForAsync(SouffleurModule? module, CancellationToken token = default)
    {
        List<DocumentFolder> standards = await context.DocumentFolders
            .Include(f => f.Groups)
            .Where(f => f.IsDefault)
            .ToListAsync(token);

        return standards.FirstOrDefault(f => f.Module == module)
               ?? standards.FirstOrDefault(f => f.Module == SouffleurModule.Documents);
    }

    public Task<DocumentFolder?> FindAsync(Guid id, CancellationToken token = default) =>
        context.DocumentFolders.Include(f => f.Groups).FirstOrDefaultAsync(f => f.Id == id, token);

    /// <summary>Zu welchem Modul ein Ziel gehoert -- danach richtet sich der Standardordner.</summary>
    public static SouffleurModule ModuleOf(DocumentTarget target) => target switch
    {
        DocumentTarget.Contact => SouffleurModule.Contacts,
        DocumentTarget.Task => SouffleurModule.Workspace,
        DocumentTarget.Event => SouffleurModule.Calendar,
        DocumentTarget.JournalEntry or DocumentTarget.Voucher => SouffleurModule.Finance,
        DocumentTarget.TourStop => SouffleurModule.Touring,
        DocumentTarget.Production => SouffleurModule.Production,
        _ => SouffleurModule.Documents,
    };

    // ── Einlesen ─────────────────────────────────────────────────────────

    /// <summary>Alle Ordner abgleichen. Was scheitert, scheitert je Ordner, nicht fuer alle.</summary>
    public async Task<Dictionary<Guid, string>> ScanAllAsync(CancellationToken token = default)
    {
        Dictionary<Guid, string> ergebnisse = [];
        List<Guid> ids = await context.DocumentFolders.Select(f => f.Id).ToListAsync(token);

        foreach (Guid id in ids)
        {
            ergebnisse[id] = await ScanAsync(id, token);
        }

        return ergebnisse;
    }

    /// <summary>Einen Ordner abgleichen; das Ergebnis steht danach auch am Ordner.</summary>
    public async Task<string> ScanAsync(Guid folderId, CancellationToken token = default)
    {
        await Einer.WaitAsync(token);
        try
        {
            DocumentFolder? folder = await context.DocumentFolders
                .Include(f => f.Groups)
                .FirstOrDefaultAsync(f => f.Id == folderId, token);
            if (folder is null)
            {
                return "Den Ordner gibt es nicht mehr.";
            }

            string text;
            try
            {
                ScanResult result = await AbgleichenAsync(folder, token);
                text = result.ToString();
            }
            catch (DocumentFileException exception)
            {
                text = exception.Message;
            }
            catch (IOException exception)
            {
                logger.LogWarning(exception, "Ordner {Path}: Einlesen gescheitert.", folder.Path);
                text = "Der Ordner ließ sich nicht lesen: " + exception.Message;
            }
            catch (UnauthorizedAccessException)
            {
                text = "souffleur darf den Ordner nicht lesen. Die Rechte der Freigabe gehören geprüft.";
            }

            folder.LastScanAt = time.GetUtcNow();
            folder.LastScanResult = text.Length > 500 ? text[..500] : text;
            await context.SaveChangesAsync(token);
            return text;
        }
        finally
        {
            Einer.Release();
        }
    }

    private async Task<ScanResult> AbgleichenAsync(DocumentFolder folder, CancellationToken token)
    {
        DateTimeOffset jetzt = time.GetUtcNow();
        List<FolderFile> dateien = storage.Enumerate(folder.Path);

        List<Document> bekannt = await context.Documents
            .Where(d => d.FolderId == folder.Id)
            .ToListAsync(token);

        Dictionary<string, Document> nachPfad = bekannt
            .Where(d => d.FolderPath != null)
            .ToDictionary(d => d.FolderPath!, StringComparer.Ordinal);

        HashSet<string> vorhanden = [.. dateien.Select(f => f.RelativePath)];

        // Die Kandidaten fuer „umbenannt oder verschoben": Dokumente, deren
        // Datei nicht mehr dort liegt, wo souffleur sie kennt -- in diesem
        // Ordner oder in einem anderen.
        List<Document> verwaist =
        [
            .. bekannt.Where(d => d.FolderPath is null || !vorhanden.Contains(d.FolderPath)),
            .. (await context.Documents
                    .Include(d => d.Folder)
                    .Where(d => d.FolderId != null && d.FolderId != folder.Id && d.MissingSince != null)
                    .ToListAsync(token)),
        ];

        int neu = 0, verschoben = 0, geaendert = 0, wieder = 0;

        foreach (FolderFile datei in dateien)
        {
            if (jetzt - datei.ModifiedAt < Ruhezeit)
            {
                continue;
            }

            if (nachPfad.TryGetValue(datei.RelativePath, out Document? doc))
            {
                if (doc.MissingSince is not null)
                {
                    doc.MissingSince = null;
                    wieder++;
                }

                if (doc.SizeInBytes != datei.Size || doc.FileModifiedAt != datei.ModifiedAt)
                {
                    // Neu gespeichert in der File Station -- dasselbe Dokument,
                    // anderer Inhalt.
                    doc.Sha256 = await storage.HashAsync(folder.Path, datei.RelativePath, token);
                    doc.SizeInBytes = datei.Size;
                    doc.FileModifiedAt = datei.ModifiedAt;
                    geaendert++;
                }

                continue;
            }

            string hash = await storage.HashAsync(folder.Path, datei.RelativePath, token);
            Document? wanderer = verwaist.FirstOrDefault(d => d.Sha256 == hash);

            if (wanderer is not null)
            {
                verwaist.Remove(wanderer);
                wanderer.FolderId = folder.Id;
                wanderer.FolderPath = datei.RelativePath;
                wanderer.FileModifiedAt = datei.ModifiedAt;
                wanderer.MissingSince = null;
                verschoben++;
                continue;
            }

            string name = Path.GetFileName(datei.RelativePath);
            var dokument = new Document
            {
                Name = Path.GetFileNameWithoutExtension(name) is { Length: > 0 } stamm ? Kurz(stamm, 200) : Kurz(name, 200),
                OriginalName = Kurz(name, 255),

                // Was die eigene Ablage nicht annimmt, wird trotzdem gefuehrt --
                // es liegt ja schon da --, aber nur als Datei zum Herunterladen.
                ContentType = DocumentStorage.Classify(name)?.ContentType ?? "application/octet-stream",
                SizeInBytes = datei.Size,
                StoredAs = string.Empty,
                Sha256 = hash,
                FolderId = folder.Id,
                FolderPath = datei.RelativePath,
                FileModifiedAt = datei.ModifiedAt,
                IsRestricted = folder.IsRestricted,
            };

            if (folder.IsRestricted)
            {
                dokument.Groups.AddRange(folder.Groups.Select(g =>
                    new DocumentGroup { DocumentId = dokument.Id, GroupId = g.GroupId }));
            }

            context.Documents.Add(dokument);
            neu++;
        }

        int fehlen = 0;
        foreach (Document doc in verwaist.Where(d => d.FolderId == folder.Id))
        {
            doc.MissingSince ??= jetzt;
            fehlen++;
        }

        await context.SaveChangesAsync(token);
        return new ScanResult(neu, verschoben, geaendert, fehlen, wieder);
    }

    // ── Belege ───────────────────────────────────────────────────────────

    /// <summary>
    /// Legt eine lesbare Kopie der Belegdatei in den Finanzordner -- das
    /// Original bleibt unveraenderlich bei souffleur (§ 146 Abs. 4 AO).
    /// </summary>
    /// <remarks>
    /// Die Kopie ist ein Dokument am Beleg und immer eingeschraenkt: auf die
    /// Gruppen des Finanzordners, ohne Gruppen auf die Verwaltung. Eine
    /// Rechnung mit Betrag und Lieferant geht nicht jeden im Haus etwas an,
    /// nur weil der Ordner fuer alle offen ist.
    ///
    /// Scheitert sie, scheitert nur die Kopie, nie der Beleg.
    /// </remarks>
    /// <returns>Ob eine Kopie entstanden ist.</returns>
    public async Task<bool> CopyVoucherAsync(Guid voucherId, CancellationToken token = default)
    {
        DocumentFolder? folder = await context.DocumentFolders
            .Include(f => f.Groups)
            .FirstOrDefaultAsync(f => f.Module == SouffleurModule.Finance && f.IsDefault, token);
        if (folder is null)
        {
            return false;
        }

        Voucher? voucher = await context.Vouchers
            .Include(v => v.File)
            .Include(v => v.Contact)
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == voucherId, token);
        if (voucher?.File is null)
        {
            return false;
        }

        bool schonDa = await context.Documents.AnyAsync(d =>
            d.FolderId != null
            && d.Links.Any(l => l.Target == DocumentTarget.Voucher && l.TargetId == voucherId), token);
        if (schonDa)
        {
            return false;
        }

        try
        {
            string endung = Path.GetExtension(voucher.File.OriginalName);
            if (string.IsNullOrEmpty(endung))
            {
                endung = voucher.File.ContentType switch
                {
                    "image/jpeg" => ".jpg",
                    "image/png" => ".png",
                    _ => ".pdf",
                };
            }

            string wer = voucher.Contact?.Name is { Length: > 0 } kontakt ? $" {kontakt}" : string.Empty;
            string name = Kurz($"{voucher.Label}{wer} – {voucher.Description}", 150) + endung;

            await using Stream original = vouchers.OpenRead(voucher.File);
            string pfad = await storage.WriteAsync(folder.Path, $"Belege/{voucher.FiscalYear}", name, original, token);
            FileInfo info = storage.Info(folder.Path, pfad);

            var dokument = new Document
            {
                Name = Kurz(Path.GetFileNameWithoutExtension(pfad.Split('/')[^1]), 200),
                OriginalName = Kurz(pfad.Split('/')[^1], 255),
                ContentType = voucher.File.ContentType,
                SizeInBytes = info.Length,
                StoredAs = string.Empty,
                Sha256 = voucher.File.Sha256,
                FolderId = folder.Id,
                FolderPath = pfad,
                FileModifiedAt = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                IsRestricted = true,
                Note = "Kopie des Belegs. Das Original bleibt unveränderlich in souffleur.",
            };
            dokument.Links.Add(new DocumentLink { DocumentId = dokument.Id, Target = DocumentTarget.Voucher, TargetId = voucherId });
            dokument.Groups.AddRange(folder.Groups.Select(g => new DocumentGroup { DocumentId = dokument.Id, GroupId = g.GroupId }));

            context.Documents.Add(dokument);
            await context.SaveChangesAsync(token);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DocumentFileException)
        {
            logger.LogWarning(exception, "Beleg {Voucher}: Kopie in den Finanzordner gescheitert.", voucher.Label);
            return false;
        }
    }

    /// <summary>Kopien fuer alle Belege, die noch keine haben -- nach dem Einrichten des Finanzordners.</summary>
    public async Task<int> CopyAllVouchersAsync(CancellationToken token = default)
    {
        List<Guid> ids = await context.Vouchers
            .Where(v => v.File != null)
            .OrderBy(v => v.FiscalYear).ThenBy(v => v.Number)
            .Select(v => v.Id)
            .ToListAsync(token);

        int kopiert = 0;
        foreach (Guid id in ids)
        {
            if (await CopyVoucherAsync(id, token))
            {
                kopiert++;
            }
        }

        return kopiert;
    }

    private static string Kurz(string text, int max) => text.Length <= max ? text : text[..max];
}
