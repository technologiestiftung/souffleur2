using Souffleur.Domain.Common;
using Souffleur.Domain.Identity;

namespace Souffleur.Domain.Documents;

/// <summary>Woran ein Dokument haengen kann.</summary>
/// <remarks>
/// Ein Verweis mit Typ und Id statt einer Fremdschluesselspalte je Ziel. Der
/// Preis ist, dass die Datenbank die Verknuepfung nicht pruefen kann -- das
/// tut der Dienst beim Anlegen, und ein verwaistes Ziel macht aus dem
/// Dokument nichts Kaputtes, nur eines mit einem Verweis ins Leere.
///
/// Der Gegenwert: Ein neues Ziel ist ein Enum-Wert und eine Zeile im Dienst,
/// keine Migration.
/// </remarks>
public enum DocumentTarget
{
    Contact = 1,
    Task = 2,
    Event = 3,

    /// <summary>Eine Buchung im Journal.</summary>
    JournalEntry = 4,

    /// <summary>Ein Beleg. Die Belegdatei selbst bleibt beim Beleg.</summary>
    Voucher = 5,

    /// <summary>Ein Gastspiel: Vertrag, Technical Rider, Hotelbestaetigung.</summary>
    TourStop = 6,

    /// <summary>Eine Produktion: Textbuch, Buehnenbildentwurf, Foerderbescheid.</summary>
    Production = 7,
}

/// <summary>
/// Ein abgelegtes Dokument des Hauses.
/// </summary>
/// <remarks>
/// Nicht zu verwechseln mit einer Belegdatei: Ein Beleg haengt
/// aufbewahrungspflichtig an seiner Buchung, ist unveraenderlich und wird
/// nicht geloescht. Ein Dokument ist freier -- es darf umbenannt, neu
/// beschriftet und weggeworfen werden. Verknuepfen lassen sich beide, siehe
/// <see cref="DocumentTarget.Voucher"/>.
///
/// Sichtbar ist ein Dokument fuer alle, solange es an keiner Gruppe haengt.
/// Haengt es an Gruppen, sehen es deren Mitglieder, wer es abgelegt hat, und
/// wer fremde Dokumente verwalten darf. Die Wahl faellt je Dokument und
/// nicht aus der Verknuepfung: Ein Dokument kann an mehreren Dingen haengen,
/// und zwei Verknuepfungen koennten sich widersprechen.
/// </remarks>
public class Document : Entity
{
    /// <summary>Wie es in der Liste heisst. Anfangs der Dateiname, aenderbar.</summary>
    public required string Name { get; set; }

    /// <summary>Wie die Datei beim Hochladen hiess -- zum Wiedererkennen.</summary>
    public required string OriginalName { get; set; }

    public required string ContentType { get; set; }

    public required long SizeInBytes { get; set; }

    /// <summary>Der Name im Ablageverzeichnis. Nie vom Hochladenden.</summary>
    public required string StoredAs { get; set; }

    /// <summary>SHA-256, um dieselbe Datei zweimal zu erkennen.</summary>
    public required string Sha256 { get; set; }

    public string? Note { get; set; }

    public List<DocumentTag> Tags { get; set; } = [];

    public List<DocumentLink> Links { get; set; } = [];

    /// <summary>Welche Gruppen es sehen duerfen, wenn es eingeschraenkt ist.</summary>
    public List<DocumentGroup> Groups { get; set; } = [];

    /// <summary>
    /// Ob das Dokument nicht fuer alle ist.
    /// </summary>
    /// <remarks>
    /// Ein eigenes Feld und nicht „hat Gruppen": Sonst wuerde das Loeschen
    /// der Gruppe „Personalakten" deren Dokumente fuer das ganze Haus
    /// oeffnen. So bleibt ein eingeschraenktes Dokument eingeschraenkt, auch
    /// wenn ihm die Gruppen abhandenkommen -- dann sehen es nur noch, wer es
    /// abgelegt hat, und wer fremde Dokumente verwalten darf.
    /// </remarks>
    public bool IsRestricted { get; set; }

    /// <summary>
    /// Der Ordner auf der Synology, in dem die Datei liegt. Ohne Ordner liegt
    /// sie in souffleurs eigener Ablage unter <see cref="StoredAs"/>.
    /// </summary>
    public Guid? FolderId { get; set; }

    public DocumentFolder? Folder { get; set; }

    /// <summary>
    /// Wo im Ordner, mit „/" getrennt -- der Name, den die File Station
    /// zeigt. Aendert sich, wenn dort jemand umbenennt oder verschiebt.
    /// </summary>
    public string? FolderPath { get; set; }

    /// <summary>
    /// Wann die Datei zuletzt geaendert wurde, wie das Dateisystem es sagt.
    /// Das Einlesen rechnet den Pruefwert nur neu, wenn sich das oder die
    /// Groesse aendert -- sonst laese es bei jedem Durchlauf alles.
    /// </summary>
    public DateTimeOffset? FileModifiedAt { get; set; }

    /// <summary>
    /// Seit wann die Datei im Ordner fehlt. Das Dokument bleibt mit seinen
    /// Schlagworten und Verknuepfungen stehen: Taucht die Datei wieder auf --
    /// zurueck aus dem Papierkorb, zurueckverschoben --, ist alles noch da.
    /// </summary>
    public DateTimeOffset? MissingSince { get; set; }
}

/// <summary>Ein Schlagwort an einem Dokument.</summary>
public class DocumentTag : Entity
{
    public required Guid DocumentId { get; set; }

    /// <summary>
    /// Kleingeschrieben und getrimmt abgelegt, damit „Vertrag" und „vertrag"
    /// dasselbe sind.
    /// </summary>
    public required string Tag { get; set; }

    public static string Normalize(string tag) => tag.Trim().ToLowerInvariant();
}

/// <summary>Die Verbindung zwischen einem Dokument und einem Gegenstand im Haus.</summary>
public class DocumentLink : Entity
{
    public required Guid DocumentId { get; set; }

    public required DocumentTarget Target { get; set; }

    public required Guid TargetId { get; set; }
}

/// <summary>Eine Gruppe, die ein eingeschraenktes Dokument sehen darf.</summary>
public class DocumentGroup : Entity
{
    public required Guid DocumentId { get; set; }

    public required Guid GroupId { get; set; }

    public Group? Group { get; set; }
}
