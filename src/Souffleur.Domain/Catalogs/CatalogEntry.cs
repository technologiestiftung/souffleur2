using Souffleur.Domain.Common;

namespace Souffleur.Domain.Catalogs;

/// <summary>
/// Ein Eintrag in einer der Listen des Hauses -- eine Terminkategorie, eine
/// Aufgabenart, eine Kostenart. Siehe ADR-0005.
/// </summary>
/// <remarks>
/// Alle Listen teilen sich diese Tabelle, unterschieden durch
/// <see cref="Catalog"/>. Das spart nicht nur Tabellen: Verwaltung, Sortierung
/// und die Regel „abschalten statt löschen“ gibt es dadurch genau einmal.
/// </remarks>
public class CatalogEntry : Entity
{
    public required CatalogKind Catalog { get; set; }

    /// <summary>
    /// Technischer Schlüssel, innerhalb der Liste eindeutig und unveränderlich.
    /// </summary>
    /// <remarks>
    /// Steht neben der <see cref="Entity.Id"/>, weil beide etwas anderes können:
    /// Die Id verbindet Datensätze, der Schlüssel erlaubt Code und
    /// Beispieldaten, einen bestimmten Eintrag zu meinen, ohne seine Id zu
    /// kennen. Die Beschriftung taugt dafür nicht -- sie darf sich ändern.
    /// </remarks>
    public required string Key { get; set; }

    /// <summary>Was in der Oberfläche steht. Darf jederzeit geändert werden.</summary>
    public required string Label { get; set; }

    /// <summary>Kurzzeichen oder Nummer, wo das Haus eines führt.</summary>
    public string? Code { get; set; }

    public string? Description { get; set; }

    /// <summary>Als <c>#rrggbb</c>. Trägt das Jahresraster des Kalenders.</summary>
    public string? Color { get; set; }

    /// <summary>
    /// Die Reihenfolge des Hauses. Alphabetisch wäre falsch: „Generalprobe“
    /// käme vor „Probe“, obwohl sie danach stattfindet.
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Abgeschaltet heißt: nicht mehr auswählbar, aber an allem weiter lesbar,
    /// was ihn schon trägt.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Mitgeliefert. Umbenennbar und abschaltbar, aber nicht löschbar -- damit
    /// eine Installation nicht ohne jede Kategorie dasteht.
    /// </summary>
    public bool IsSystem { get; set; }

    /// <summary>Übergeordneter Eintrag, wo die Liste eine Gliederung kennt.</summary>
    public Guid? ParentId { get; set; }

    public CatalogEntry? Parent { get; set; }

    public string Display => Code is null ? Label : $"{Code} {Label}";
}
