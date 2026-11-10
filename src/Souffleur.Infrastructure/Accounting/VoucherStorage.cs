using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Souffleur.Domain.Accounting;

namespace Souffleur.Infrastructure.Accounting;

/// <summary>
/// Legt Belegdateien im Datenverzeichnis ab und holt sie wieder.
/// </summary>
/// <remarks>
/// Drei Regeln, und alle drei haben einen Grund:
///
/// Der Dateiname kommt nie vom Hochladenden. Er wird aus der Belegkennung gebildet;
/// ein Name wie <c>../../appsettings.json</c> hat sonst Zugriff auf alles.
///
/// Es werden nur die Typen angenommen, die ein Beleg sein kann. Ein hochgeladenes
/// HTML oder SVG wuerde beim Anzeigen im Browser als Seite ausgefuehrt.
///
/// Eine vorhandene Datei wird nie ueberschrieben. Belege unterliegen der
/// Aufbewahrungspflicht; ein Beleg, den man ersetzen kann, ist keiner.
/// </remarks>
public class VoucherStorage(IConfiguration configuration, ILogger<VoucherStorage> logger)
{
    /// <summary>Was ein Beleg sein darf, mit der Endung, unter der er abgelegt wird.</summary>
    private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = ".pdf",
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/heic"] = ".heic",
        ["image/tiff"] = ".tiff",
        // XRechnung ist reines XML, ZUGFeRD ein PDF mit eingebettetem XML.
        ["application/xml"] = ".xml",
        ["text/xml"] = ".xml",
    };

    /// <summary>
    /// 25 MB. Ein Foto vom Handy liegt darunter, ein eingescannter Ordner nicht --
    /// und der gehoert auch nicht als ein Beleg erfasst.
    /// </summary>
    public const long MaxSizeInBytes = 25 * 1024 * 1024;

    public static bool IsAllowedType(string contentType) => AllowedTypes.ContainsKey(contentType);

    public static IReadOnlyCollection<string> AllowedContentTypes => AllowedTypes.Keys;

    private string Root => Path.Combine(
        configuration["Souffleur:DataDirectory"] ?? "data", "vouchers");

    /// <summary>
    /// Schreibt die Datei und gibt zurueck, was darueber in die Datenbank gehoert.
    /// </summary>
    public async Task<VoucherFile> StoreAsync(
        Voucher voucher,
        Stream content,
        string originalName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        if (!AllowedTypes.TryGetValue(contentType, out string? extension))
        {
            throw new VoucherFileException(
                $"Dateien vom Typ {contentType} sind als Beleg nicht vorgesehen. "
                + $"Möglich sind: {string.Join(", ", AllowedTypes.Keys)}.");
        }

        // Nach Jahr abgelegt, damit das Verzeichnis nach acht Jahren
        // Aufbewahrung noch zu ueberblicken ist.
        string directory = Path.Combine(Root, voucher.FiscalYear.ToString());
        Directory.CreateDirectory(directory);

        string fileName = $"{voucher.Number:D5}-{voucher.Id:N}{extension}";
        string fullPath = Path.Combine(directory, fileName);

        if (File.Exists(fullPath))
        {
            throw new VoucherFileException(
                $"Zu Beleg {voucher.Label} liegt bereits eine Datei. Belege werden nicht ersetzt.");
        }

        long size;
        string hash;
        await using (FileStream target = new(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await content.CopyToAsync(target, cancellationToken);
            size = target.Length;
        }

        await using (FileStream written = File.OpenRead(fullPath))
        {
            hash = Convert.ToHexString(await SHA256.HashDataAsync(written, cancellationToken)).ToLowerInvariant();
        }

        if (size == 0)
        {
            File.Delete(fullPath);
            throw new VoucherFileException("Die Datei ist leer.");
        }

        if (size > MaxSizeInBytes)
        {
            File.Delete(fullPath);
            throw new VoucherFileException(
                $"Die Datei ist mit {size / 1024 / 1024} MB zu groß; erlaubt sind {MaxSizeInBytes / 1024 / 1024} MB.");
        }

        logger.LogInformation(
            "Beleg {Voucher}: Datei abgelegt, {Size} Bytes, SHA-256 {Hash}.",
            voucher.Label, size, hash);

        return new VoucherFile
        {
            VoucherId = voucher.Id,
            RelativePath = $"{voucher.FiscalYear}/{fileName}",
            OriginalName = SafeDisplayName(originalName),
            ContentType = contentType,
            SizeInBytes = size,
            Sha256 = hash,
        };
    }

    public Stream OpenRead(VoucherFile file) =>
        File.OpenRead(ResolvePath(file));

    public bool Exists(VoucherFile file) => File.Exists(ResolvePath(file));

    /// <summary>
    /// Rechnet den Pruefwert nach. Weicht er ab, wurde die Datei seit dem Ablegen
    /// veraendert -- das muss das Haus erfahren, nicht erst die Betriebspruefung.
    /// </summary>
    public async Task<bool> VerifyAsync(VoucherFile file, CancellationToken cancellationToken = default)
    {
        string path = ResolvePath(file);
        if (!File.Exists(path))
        {
            return false;
        }

        await using FileStream stream = File.OpenRead(path);
        string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();

        if (hash != file.Sha256)
        {
            logger.LogError(
                "Belegdatei {Path} stimmt nicht mehr mit ihrem Prüfwert überein. "
                + "Erwartet {Expected}, gefunden {Actual}.",
                file.RelativePath, file.Sha256, hash);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Loest den gespeicherten Pfad auf und stellt sicher, dass er im
    /// Belegverzeichnis bleibt.
    /// </summary>
    private string ResolvePath(VoucherFile file)
    {
        string root = Path.GetFullPath(Root);
        string full = Path.GetFullPath(Path.Combine(root, file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));

        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new VoucherFileException("Der Pfad dieses Belegs zeigt aus dem Belegverzeichnis heraus.");
        }

        return full;
    }

    /// <summary>
    /// Macht aus dem hochgeladenen Namen etwas, das sich gefahrlos anzeigen laesst.
    /// Er wird nie als Pfad verwendet, kann aber in Protokollen und Oberflaechen landen.
    /// </summary>
    private static string SafeDisplayName(string name)
    {
        string trimmed = Path.GetFileName(name.Trim());
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "beleg";
        }

        char[] cleaned = [.. trimmed
            .Where(c => !char.IsControl(c))
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)];

        return new string(cleaned)[..Math.Min(cleaned.Length, 255)];
    }
}

public class VoucherFileException(string message) : InvalidOperationException(message);
