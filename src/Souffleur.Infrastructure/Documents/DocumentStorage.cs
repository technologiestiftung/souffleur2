using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;

namespace Souffleur.Infrastructure.Documents;

/// <summary>Eine Datei liess sich nicht ablegen -- mit dem Grund in Worten.</summary>
public class DocumentFileException(string message) : Exception(message);

/// <summary>
/// Legt Dokumente im Datenverzeichnis ab und holt sie wieder.
/// </summary>
/// <remarks>
/// Dieselben drei Regeln wie bei den Belegen, eine davon gelockert:
///
/// Der Dateiname kommt nie vom Hochladenden. Ein Name wie
/// <c>../../appsettings.json</c> haette sonst Zugriff auf alles.
///
/// Nur bekannte Arten. Weiter gefasst als bei Belegen -- ein Haus legt
/// Vertraege, Tabellen, Fotos und Mitschnitte ab --, aber nie etwas, das der
/// Browser als Seite ausfuehren wuerde: kein HTML, kein SVG, kein Skript.
///
/// Anders als ein Beleg darf ein Dokument wieder weg. Es unterliegt keiner
/// Aufbewahrungspflicht, und eine Ablage, aus der nichts verschwindet, wird
/// nicht aufgeraeumt, sondern gemieden.
/// </remarks>
public class DocumentStorage(IConfiguration configuration)
{
    /// <summary>Endung → Art. Die Endung entscheidet, wenn der Browser keine Art mitschickt.</summary>
    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".heic"] = "image/heic",
        [".tif"] = "image/tiff",
        [".tiff"] = "image/tiff",
        [".txt"] = "text/plain",
        [".csv"] = "text/csv",
        [".rtf"] = "application/rtf",
        [".xml"] = "application/xml",
        [".zip"] = "application/zip",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".odt"] = "application/vnd.oasis.opendocument.text",
        [".ods"] = "application/vnd.oasis.opendocument.spreadsheet",
        [".odp"] = "application/vnd.oasis.opendocument.presentation",
        [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav",
        [".m4a"] = "audio/mp4",
        [".mp4"] = "video/mp4",
        [".mov"] = "video/quicktime",
    };

    /// <summary>
    /// 100 MB. Ein Probenmitschnitt passt, ein abendfuellender Film nicht --
    /// der gehoert nicht in eine Ablage auf der Synology des Hauses.
    /// </summary>
    public const long MaxSizeInBytes = 100L * 1024 * 1024;

    public static IReadOnlyCollection<string> AllowedExtensions => Types.Keys;

    private string Root => Path.Combine(configuration["Souffleur:DataDirectory"] ?? "data", "documents");

    /// <summary>Die Art, unter der eine Datei abgelegt wird -- oder null, wenn sie nicht hineingehoert.</summary>
    public static (string Extension, string ContentType)? Classify(string? originalName)
    {
        string extension = Path.GetExtension(originalName ?? string.Empty);
        return Types.TryGetValue(extension, out string? type) ? (extension.ToLowerInvariant(), type) : null;
    }

    public record Stored(string StoredAs, string ContentType, long Size, string Sha256, string OriginalName);

    /// <summary>
    /// Schreibt die Datei. Die Art folgt der Endung und nicht dem, was der
    /// Browser behauptet: Der schickt fuer eine .docx oft nur
    /// „application/octet-stream", und eine umbenannte .html bliebe sonst als
    /// „text/html" durchlaessig.
    /// </summary>
    public async Task<Stored> StoreAsync(
        Guid documentId,
        Stream content,
        string? originalName,
        CancellationToken cancellationToken = default)
    {
        if (Classify(originalName) is not { } art)
        {
            throw new DocumentFileException(
                $"Dateien wie „{SafeName(originalName)}“ gehören nicht in die Ablage. Möglich sind: "
                + string.Join(", ", Types.Keys.Select(k => k.TrimStart('.')).Distinct()) + ".");
        }

        string year = DateTime.UtcNow.Year.ToString();
        string directory = Path.Combine(Root, year);
        Directory.CreateDirectory(directory);

        string fileName = $"{documentId:N}{art.Extension}";
        string fullPath = Path.Combine(directory, fileName);

        long size;
        await using (FileStream target = new(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            // Mitzaehlen beim Kopieren, statt erst alles zu schreiben: Eine
            // Datei von zwei Gigabyte soll nicht erst die Platte fuellen, um
            // dann abgewiesen zu werden.
            byte[] puffer = new byte[81920];
            size = 0;
            int gelesen;
            while ((gelesen = await content.ReadAsync(puffer, cancellationToken)) > 0)
            {
                size += gelesen;
                if (size > MaxSizeInBytes)
                {
                    break;
                }

                await target.WriteAsync(puffer.AsMemory(0, gelesen), cancellationToken);
            }
        }

        if (size == 0 || size > MaxSizeInBytes)
        {
            File.Delete(fullPath);
            throw new DocumentFileException(size == 0
                ? "Die Datei ist leer."
                : $"Die Datei ist zu groß; erlaubt sind {MaxSizeInBytes / 1024 / 1024} MB.");
        }

        string hash;
        await using (FileStream written = File.OpenRead(fullPath))
        {
            hash = Convert.ToHexString(await SHA256.HashDataAsync(written, cancellationToken)).ToLowerInvariant();
        }

        return new Stored($"{year}/{fileName}", art.ContentType, size, hash, SafeName(originalName));
    }

    public Stream OpenRead(string storedAs) => File.OpenRead(Resolve(storedAs));

    public bool Exists(string storedAs) => File.Exists(Resolve(storedAs));

    public void Delete(string storedAs)
    {
        string path = Resolve(storedAs);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>Haelt den Pfad im Ablageverzeichnis, egal was in der Datenbank steht.</summary>
    private string Resolve(string storedAs)
    {
        string root = Path.GetFullPath(Root);
        string full = Path.GetFullPath(Path.Combine(root, storedAs));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new DocumentFileException("Der Ablageort liegt außerhalb der Ablage.");
        }

        return full;
    }

    /// <summary>Nur der Dateiname, ohne Pfad und Steuerzeichen -- er wird angezeigt.</summary>
    private static string SafeName(string? name)
    {
        string kurz = Path.GetFileName(name ?? string.Empty);
        string sauber = new([.. kurz.Where(c => !char.IsControl(c))]);
        return string.IsNullOrWhiteSpace(sauber) ? "Datei" : sauber[..Math.Min(sauber.Length, 255)];
    }
}
