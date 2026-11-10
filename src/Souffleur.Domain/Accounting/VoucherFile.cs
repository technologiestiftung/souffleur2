using Souffleur.Domain.Common;

namespace Souffleur.Domain.Accounting;

/// <summary>
/// Die Datei hinter einem Beleg. Liegt im Datenverzeichnis, nicht in der Datenbank.
/// </summary>
/// <remarks>
/// Einmal abgelegt wird sie nie wieder angefasst. Der Pruefwert haelt fest, womit
/// sie abgelegt wurde: Wer spaeter wissen will, ob eine Datei noch dieselbe ist,
/// rechnet nach. Das ist die Unveraenderbarkeit aus den GoBD, auf die Datei
/// angewandt -- ein Dateisystem kennt keine Sperre dafuer.
/// </remarks>
public class VoucherFile : Entity
{
    public Guid VoucherId { get; set; }

    public Voucher Voucher { get; set; } = null!;

    /// <summary>
    /// Pfad unterhalb des Datenverzeichnisses, mit Schraegstrichen. Nie ein
    /// absoluter Pfad -- sonst zieht ein Umzug des Volumes alles mit.
    /// </summary>
    public required string RelativePath { get; set; }

    /// <summary>Wie die Datei beim Hochladen hiess. Nur zur Anzeige.</summary>
    public required string OriginalName { get; set; }

    public required string ContentType { get; set; }

    public required long SizeInBytes { get; set; }

    /// <summary>SHA-256 der Datei, in Kleinbuchstaben.</summary>
    public required string Sha256 { get; set; }
}
