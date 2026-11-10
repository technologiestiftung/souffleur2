using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;

namespace Souffleur.Infrastructure.Documents;

/// <summary>Eine Datei, wie sie im Ordner liegt.</summary>
/// <param name="RelativePath">Innerhalb des Ordners, mit „/" getrennt.</param>
public sealed record FolderFile(string RelativePath, long Size, DateTimeOffset ModifiedAt);

/// <summary>Ein Verzeichnis unterhalb der Ablage -- fuer die Auswahl in den Einstellungen.</summary>
public sealed record FolderEntry(string Name, string Path, bool HasChildren);

/// <summary>
/// Liest und schreibt in den Ordnern, die das Haus von seiner Synology in den
/// Container einhaengt (ADR-0013).
/// </summary>
/// <remarks>
/// Alle Freigaben haengen unter einer Wurzel (<c>Souffleur:Ablage:Pfad</c>,
/// im Container <c>/ablage</c>). souffleur sieht nur, was darunter liegt --
/// der Pfad eines Ordners kommt aus der Datenbank und wird bei jedem Zugriff
/// gegen die Wurzel geprueft, wie bei der eigenen Ablage.
///
/// Die Synology legt eigene Verzeichnisse in jeden Ordner: <c>@eaDir</c> fuer
/// Vorschaubilder, <c>#recycle</c> fuer den Papierkorb, <c>#snapshot</c>.
/// Die gehoeren nicht zum Haus und werden uebergangen, ebenso versteckte
/// Dateien und die Sperrdateien, die Office beim Bearbeiten anlegt.
/// </remarks>
public class FolderStorage(IConfiguration configuration)
{
    /// <summary>Mehr liest ein Durchlauf je Ordner nicht -- ein falsch gewaehlter Ordner soll nicht das ganze NAS durchwuehlen.</summary>
    public const int MaxFilesPerFolder = 20_000;

    private const int MaxDepth = 12;

    private static readonly HashSet<string> Uebergehen = new(StringComparer.OrdinalIgnoreCase)
    {
        "@eaDir", "#recycle", "#snapshot", "@tmp", "@SynoResource", ".SynologyWorkingDirectory",
        "Thumbs.db", "desktop.ini", ".DS_Store",
    };

    /// <summary>Die Wurzel, unter der die Freigaben eingehaengt sind.</summary>
    public string Root => Path.GetFullPath(
        configuration["Souffleur:Ablage:Pfad"]
        ?? Path.Combine(configuration["Souffleur:DataDirectory"] ?? "data", "ablage"));

    /// <summary>
    /// Ob es die Wurzel gibt. Ohne Einstellung liegt sie im Datenverzeichnis
    /// und wird angelegt -- dann arbeiten Testinstanzen und Entwicklung ohne
    /// Synology mit Ordnern im eigenen Volume. Ist ein Pfad eingestellt und
    /// fehlt, ist etwas beim Einhaengen schiefgegangen: Dann nichts anlegen,
    /// sonst schriebe souffleur still in den Container statt auf das NAS.
    /// </summary>
    public bool IsAvailable
    {
        get
        {
            if (configuration["Souffleur:Ablage:Pfad"] is null)
            {
                Directory.CreateDirectory(Root);
            }

            return Directory.Exists(Root);
        }
    }

    /// <summary>Die Verzeichnisse unter einem Pfad -- eine Ebene, zum Durchklicken.</summary>
    public IReadOnlyList<FolderEntry> Browse(string? relativePath)
    {
        _ = IsAvailable;
        string directory = Resolve(relativePath ?? string.Empty, mustExist: true);

        return
        [
            .. new DirectoryInfo(directory).EnumerateDirectories()
                .Where(d => !Skip(d.Name))
                .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(500)
                .Select(d => new FolderEntry(
                    d.Name,
                    Join(relativePath, d.Name),
                    d.EnumerateDirectories().Any(c => !Skip(c.Name)))),
        ];
    }

    public bool FolderExists(string folderPath) => Directory.Exists(Resolve(folderPath));

    /// <summary>Einen Unterordner anlegen -- aus den Einstellungen, damit niemand dafuer in die File Station muss.</summary>
    public string CreateDirectory(string? parent, string name)
    {
        // Ganz oben haengen die Freigaben der Synology. Ein Ordner dort
        // laege im Container und nicht auf dem NAS -- und waere beim
        // naechsten Update weg.
        if (string.IsNullOrEmpty(parent) && configuration["Souffleur:Ablage:Pfad"] is not null)
        {
            throw new DocumentFileException(
                "Ganz oben stehen die Freigaben der Synology. Eine neue Freigabe legt man in der DSM an "
                + "und hängt sie in den Container ein; Unterordner gehen hier.");
        }

        string sauber = SafeSegment(name);
        string relative = Join(parent, sauber);
        Directory.CreateDirectory(Resolve(relative));
        return relative;
    }

    /// <summary>Alle Dateien eines Ordners, rekursiv, ohne das, was der Synology gehoert.</summary>
    public List<FolderFile> Enumerate(string folderPath)
    {
        string root = Resolve(folderPath, mustExist: true);
        List<FolderFile> files = [];
        Walk(root, string.Empty, 0, files);
        return files;
    }

    private void Walk(string directory, string prefix, int depth, List<FolderFile> files)
    {
        if (depth > MaxDepth || files.Count >= MaxFilesPerFolder)
        {
            return;
        }

        foreach (FileSystemInfo entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
        {
            if (Skip(entry.Name) || entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                // Verknuepfungen nicht folgen: Ein Link nach oben liesse den
                // Durchlauf kreisen oder aus dem Ordner hinaus.
                continue;
            }

            string relative = prefix.Length == 0 ? entry.Name : $"{prefix}/{entry.Name}";

            if (entry is DirectoryInfo sub)
            {
                Walk(sub.FullName, relative, depth + 1, files);
            }
            else if (entry is FileInfo file && file.Length > 0)
            {
                files.Add(new FolderFile(relative, file.Length, new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)));
                if (files.Count >= MaxFilesPerFolder)
                {
                    return;
                }
            }
        }
    }

    public async Task<string> HashAsync(string folderPath, string relativePath, CancellationToken token)
    {
        await using FileStream stream = File.OpenRead(Resolve(Join(folderPath, relativePath)));
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).ToLowerInvariant();
    }

    public Stream OpenRead(string folderPath, string relativePath) =>
        File.OpenRead(Resolve(Join(folderPath, relativePath)));

    public bool Exists(string folderPath, string relativePath) =>
        File.Exists(Resolve(Join(folderPath, relativePath)));

    public FileInfo Info(string folderPath, string relativePath) =>
        new(Resolve(Join(folderPath, relativePath)));

    /// <summary>
    /// Schreibt eine Datei mit lesbarem Namen. Gibt es den Namen schon, kommt
    /// eine Zahl dazu, wie es die File Station auch macht -- ueberschrieben
    /// wird nie.
    /// </summary>
    /// <returns>Der Pfad innerhalb des Ordners.</returns>
    public async Task<string> WriteAsync(
        string folderPath,
        string? subdirectory,
        string fileName,
        Stream content,
        CancellationToken token,
        long maxBytes = long.MaxValue)
    {
        string sauber = SafeSegment(fileName);
        string unter = string.Join('/', (subdirectory ?? string.Empty)
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(SafeSegment));

        string directory = Resolve(Join(folderPath, unter));
        Directory.CreateDirectory(directory);

        string stamm = Path.GetFileNameWithoutExtension(sauber);
        string endung = Path.GetExtension(sauber);

        for (int n = 1; n < 1000; n++)
        {
            string name = n == 1 ? sauber : $"{stamm} ({n}){endung}";
            string full = Path.Combine(directory, name);

            FileStream target;
            try
            {
                target = new(full, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            }
            catch (IOException) when (File.Exists(full))
            {
                // Vergeben -- der naechste Name.
                continue;
            }

            try
            {
                await using (target)
                {
                    // Mitzaehlen beim Kopieren, wie in der eigenen Ablage: Eine
                    // zu grosse Datei soll nicht erst das NAS fuellen.
                    byte[] puffer = new byte[81920];
                    long size = 0;
                    int gelesen;
                    while ((gelesen = await content.ReadAsync(puffer, token)) > 0)
                    {
                        size += gelesen;
                        if (size > maxBytes)
                        {
                            throw new DocumentFileException(
                                $"Die Datei ist zu groß; erlaubt sind {maxBytes / 1024 / 1024} MB.");
                        }

                        await target.WriteAsync(puffer.AsMemory(0, gelesen), token);
                    }

                    if (size == 0)
                    {
                        throw new DocumentFileException("Die Datei ist leer.");
                    }
                }

                return Join(unter, name);
            }
            catch
            {
                // Eine halbe Datei im Ordner saehe in der File Station aus wie
                // eine ganze.
                File.Delete(full);
                throw;
            }
        }

        throw new DocumentFileException($"Für „{sauber}“ ist im Ordner kein freier Name zu finden.");
    }

    /// <summary>
    /// Weg damit -- in den Papierkorb der Freigabe, wenn sie einen hat.
    /// </summary>
    /// <remarks>
    /// Was die File Station loescht, landet im Papierkorb der Freigabe
    /// (<c>#recycle</c> in ihrer obersten Ebene). souffleur loescht ueber das
    /// Dateisystem, und das kennt keinen Papierkorb -- deshalb verschiebt es
    /// selbst dorthin, damit ein Fehlklick in souffleur so umkehrbar ist wie
    /// einer in der File Station.
    /// </remarks>
    public void Delete(string folderPath, string relativePath)
    {
        string full = Resolve(Join(folderPath, relativePath));
        if (!File.Exists(full))
        {
            return;
        }

        string freigabe = folderPath.Split('/', 2)[0];
        string papierkorb = Path.Combine(Resolve(freigabe), "#recycle");

        if (Directory.Exists(papierkorb))
        {
            string ziel = Path.Combine(papierkorb, "souffleur", DateTime.UtcNow.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(ziel);
            string name = Path.GetFileName(full);
            string zielDatei = Path.Combine(ziel, name);
            for (int n = 2; File.Exists(zielDatei); n++)
            {
                zielDatei = Path.Combine(ziel, $"{Path.GetFileNameWithoutExtension(name)} ({n}){Path.GetExtension(name)}");
            }

            File.Move(full, zielDatei);
            return;
        }

        File.Delete(full);
    }

    /// <summary>Haelt jeden Pfad unter der Wurzel, egal was in der Datenbank steht.</summary>
    private string Resolve(string relative, bool mustExist = false)
    {
        string root = Root;
        string full = Path.GetFullPath(Path.Combine(root, relative.Replace('\\', '/').TrimStart('/')));

        if (full != root && !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new DocumentFileException("Der Ordner liegt außerhalb der Ablage.");
        }

        if (mustExist && !Directory.Exists(full))
        {
            throw new DocumentFileException(relative.Length == 0
                ? "Unter der Ablage ist nichts eingehängt."
                : $"Den Ordner „{relative}“ gibt es auf der Synology nicht (mehr).");
        }

        return full;
    }

    private static bool Skip(string name) =>
        Uebergehen.Contains(name)
        || name.StartsWith('.')
        || name.StartsWith("~$", StringComparison.Ordinal)
        || name.StartsWith('@');

    private static string Join(string? a, string b) =>
        string.IsNullOrEmpty(a) ? b : string.IsNullOrEmpty(b) ? a : $"{a.TrimEnd('/')}/{b}";

    /// <summary>
    /// Ein Name, den Windows, die Synology und der Browser vertragen: ohne
    /// Pfadtrenner, ohne Zeichen, die SMB verbietet, nicht leer.
    /// </summary>
    public static string SafeSegment(string? name)
    {
        string kurz = Path.GetFileName((name ?? string.Empty).Replace('\\', '/'));
        char[] verboten = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];
        string sauber = new([.. kurz.Where(c => !char.IsControl(c) && !verboten.Contains(c))]);
        sauber = sauber.Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(sauber) || sauber is "." or "..")
        {
            sauber = "Datei";
        }

        return sauber[..Math.Min(sauber.Length, 200)];
    }
}
