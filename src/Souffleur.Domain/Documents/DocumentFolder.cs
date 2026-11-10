using Souffleur.Domain.Common;
using Souffleur.Domain.Identity;
using Souffleur.Domain.Modules;

namespace Souffleur.Domain.Documents;

/// <summary>
/// Ein Ordner auf der Synology des Hauses, den souffleur als Ablage benutzt
/// -- siehe ADR-0013.
/// </summary>
/// <remarks>
/// Der Ordner ist die Wahrheit, nicht souffleur. Was jemand in der File
/// Station hineinlegt, umbenennt oder verschiebt, liest souffleur nach; was
/// in souffleur hochgeladen wird, landet dort mit lesbarem Namen. souffleur
/// fuegt hinzu, was die File Station nicht kennt: Schlagworte,
/// Verknuepfungen, Sichtbarkeit.
///
/// Je Modul darf es mehrere Ordner geben, aber nur einen Standard: Er nimmt
/// auf, was aus dem Modul heraus hochgeladen wird -- ein Dokument an einer
/// Produktion landet im Produktionsordner, ohne dass jemand waehlen muss.
///
/// Der Pfad ist relativ zur Wurzel, unter der die Freigaben in den Container
/// eingehaengt sind (<c>/ablage</c>). Absolut darf er nie sein: Sonst liesse
/// sich ein Ordner auf <c>/data</c> zeigen, und die Datenbank laege offen.
/// </remarks>
public class DocumentFolder : Entity
{
    /// <summary>Wie der Ordner in souffleur heisst, etwa „Förderbescheide".</summary>
    public required string Name { get; set; }

    /// <summary>Unterhalb der Wurzel, mit „/" getrennt, etwa „Verwaltung/Finanzen".</summary>
    public required string Path { get; set; }

    public required SouffleurModule Module { get; set; }

    /// <summary>Ob hochgeladene Dokumente des Moduls hierher gehen.</summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Ob gefundene Dateien nur fuer bestimmte Gruppen sichtbar sind. Gilt fuer
    /// neu gefundene und hier abgelegte Dokumente; wer ein Dokument danach
    /// anders einstellt, behaelt seine Einstellung.
    /// </summary>
    public bool IsRestricted { get; set; }

    public List<DocumentFolderGroup> Groups { get; set; } = [];

    public DateTimeOffset? LastScanAt { get; set; }

    /// <summary>Was beim letzten Einlesen herauskam, in Worten.</summary>
    public string? LastScanResult { get; set; }

    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];

        if (string.IsNullOrWhiteSpace(Name))
        {
            problems.Add("Der Ordner braucht einen Namen.");
        }

        if (string.IsNullOrWhiteSpace(Path))
        {
            problems.Add("Es fehlt, welcher Ordner gemeint ist.");
        }
        else if (Path.StartsWith('/') || Path.Contains('\\') || Path.Split('/').Any(t => t is "" or "." or ".."))
        {
            problems.Add("Der Pfad muss unterhalb der Ablage liegen.");
        }

        if (Module is SouffleurModule.Overview or SouffleurModule.Chat)
        {
            problems.Add("Dieses Modul legt keine Dokumente ab.");
        }

        return problems;
    }

    /// <summary>Den Pfad in die Form bringen, in der er gespeichert und verglichen wird.</summary>
    public static string NormalizePath(string path) =>
        string.Join('/', path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>Eine Gruppe, die Dokumente eines eingeschraenkten Ordners sieht.</summary>
public class DocumentFolderGroup : Entity
{
    public required Guid FolderId { get; set; }

    public required Guid GroupId { get; set; }

    public Group? Group { get; set; }
}
