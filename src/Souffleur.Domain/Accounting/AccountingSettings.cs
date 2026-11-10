namespace Souffleur.Domain.Accounting;

/// <summary>
/// Die Buchhaltungsregeln dieses Hauses, so weit sie eine einzelne Buchung
/// betreffen. Aus den Stammdaten der Organisation abgeleitet und der
/// Pruefung mitgegeben, damit die Domaene nicht in der Datenbank nachsehen muss.
/// </summary>
public record AccountingSettings(ChartOfAccounts ChartOfAccounts, bool RequiresTaxSpheres)
{
    /// <summary>
    /// Was gilt, solange die Stammdaten des Hauses noch nicht erfasst sind:
    /// SKR03 ohne Sphaeren. Fuer einen Verein ist das falsch -- deshalb fragt
    /// die Einrichtung beides als Erstes ab.
    /// </summary>
    public static readonly AccountingSettings Default = new(ChartOfAccounts.Skr03, false);
}
