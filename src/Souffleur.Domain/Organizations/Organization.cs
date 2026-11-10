using Souffleur.Domain.Accounting;
using Souffleur.Domain.Common;

namespace Souffleur.Domain.Organizations;

/// <summary>
/// Das Haus, das diese Installation betreibt. Es gibt genau einen Datensatz --
/// souffleur ist nicht mandantenfaehig, jedes Haus betreibt seinen eigenen Container.
/// </summary>
/// <remarks>
/// Diese Stammdaten erscheinen auf Belegen, im DATEV-Export und als Auftraggeber
/// in SEPA-Dateien.
/// </remarks>
public class Organization : Entity
{
    /// <summary>
    /// Die Id des einen Datensatzes. Fest, weil es nur einen gibt -- siehe
    /// ADR-0003. Die Datenbank setzt das mit einem Check-Constraint durch.
    /// </summary>
    public const string SingletonId = "00000000-0000-0000-0000-000000000001";

    public Organization() => Id = Guid.Parse(SingletonId);

    public required string Name { get; set; }

    public string? LegalForm { get; set; }

    public string? Street { get; set; }

    public string? PostalCode { get; set; }

    public string? City { get; set; }

    /// <summary>Steuernummer beim Finanzamt.</summary>
    public string? TaxNumber { get; set; }

    /// <summary>Umsatzsteuer-Identifikationsnummer, falls vorhanden.</summary>
    public string? VatId { get; set; }

    /// <summary>Betriebsnummer der Kuenstlersozialkasse, falls das Haus abgabepflichtig ist.</summary>
    public string? KskNumber { get; set; }

    /// <summary>
    /// Die Zeitzone, in der das Haus arbeitet. Alle Zeitpunkte liegen in UTC;
    /// diese Angabe sagt, welcher Tag das im Kalender ist und zu welcher
    /// Uhrzeit eine Serie stattfindet.
    /// </summary>
    public string TimeZoneId { get; set; } = "Europe/Berlin";

    /// <summary>
    /// Das Bundesland -- es entscheidet ueber die Feiertage und damit darueber,
    /// wie viele Arbeitstage ein Urlaub kostet. Berlin, weil die ersten Haeuser
    /// dort sitzen.
    /// </summary>
    public FederalState FederalState { get; set; } = FederalState.BE;

    /// <summary>
    /// Ob in dieser Installation Vorführdaten stehen.
    /// </summary>
    /// <remarks>
    /// Gesetzt vom Entwicklungsbestand, und zwar dauerhaft: Wer eine solche
    /// Datenbank später im Betrieb benutzt, soll das weiter sehen. Die
    /// Oberfläche zeigt es an, damit niemand einen erfundenen Termin für einen
    /// echten hält -- im Kalender des Telefons sieht man den Unterschied sonst
    /// nicht.
    /// </remarks>
    public bool HasDemoData { get; set; }

    /// <summary>
    /// Nach welchem Kontenrahmen das Haus bucht. Vorgabe des Steuerbueros --
    /// souffleur liefert keinen mit.
    /// </summary>
    public ChartOfAccounts ChartOfAccounts { get; set; } = ChartOfAccounts.Skr03;

    /// <summary>
    /// Ob das Haus als gemeinnuetzig anerkannt ist. Dann trennt es seine
    /// Einnahmen und Ausgaben nach den vier Sphaeren (<see cref="TaxSphere"/>),
    /// und souffleur verlangt die Angabe bei jeder Erfolgsbuchung.
    /// </summary>
    public bool IsNonProfit { get; set; }

    /// <summary>
    /// Ob bei Erfolgsbuchungen eine Sphaere anzugeben ist. Gilt fuer
    /// gemeinnuetzige Haeuser und fuer jeden Kontenrahmen, der die Sphaere nicht
    /// schon in der Kontonummer traegt.
    /// </summary>
    public bool RequiresTaxSpheres => IsNonProfit || ChartOfAccounts.UsesTaxSpheres();
}
