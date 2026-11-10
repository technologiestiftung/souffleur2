namespace Souffleur.Domain.Accounting;

/// <summary>
/// Der Kontenrahmen, nach dem ein Haus bucht. Welcher gilt, sagt das Steuerbuero;
/// souffleur gibt keinen vor.
/// </summary>
/// <remarks>
/// Die Rechtsform entscheidet mit: Ein gemeinnuetziger Verein bucht anders als
/// eine GmbH, weil er seinen Gewinn nach vier Taetigkeitsbereichen trennen muss
/// (siehe <see cref="TaxSphere"/>).
/// </remarks>
public enum ChartOfAccounts
{
    /// <summary>
    /// SKR03, nach dem Geschaeftsablauf gegliedert. Der verbreitetste Rahmen
    /// fuer Gewerbebetriebe.
    /// </summary>
    Skr03 = 3,

    /// <summary>
    /// SKR04, nach der Bilanzgliederung des § 266 HGB aufgebaut.
    /// </summary>
    Skr04 = 4,

    /// <summary>
    /// SKR42, der Rahmen fuer Vereine, Stiftungen und gemeinnuetzige GmbHs.
    /// Loest den frueheren SKR49 ab.
    /// </summary>
    Skr42 = 42,

    /// <summary>
    /// Der Kontenplan des Hauses, wie ihn das Steuerbuero liefert -- weder an
    /// einen der Standardrahmen angelehnt noch an seine Nummernlogik gebunden.
    /// </summary>
    Custom = 99,
}

public static class ChartsOfAccounts
{
    public static readonly ChartOfAccounts[] All = Enum.GetValues<ChartOfAccounts>();

    public static string Label(this ChartOfAccounts chart) => chart switch
    {
        ChartOfAccounts.Skr03 => "SKR03 — Gewerbebetriebe, nach Geschäftsablauf",
        ChartOfAccounts.Skr04 => "SKR04 — Gewerbebetriebe, nach Bilanzgliederung",
        ChartOfAccounts.Skr42 => "SKR42 — Vereine, Stiftungen, gemeinnützige GmbH",
        ChartOfAccounts.Custom => "Eigener Kontenplan des Steuerbüros",
        _ => chart.ToString(),
    };

    /// <summary>
    /// Ob dieser Rahmen die Trennung nach steuerlichen Sphaeren vorsieht.
    /// </summary>
    /// <remarks>
    /// Im SKR42 steht die Sphaere nicht in der Kontonummer -- anders als im
    /// abgeloesten SKR49, wo die Kontenklasse sie mitbrachte. Sie muss deshalb
    /// an der Buchung haengen.
    /// </remarks>
    public static bool UsesTaxSpheres(this ChartOfAccounts chart) =>
        chart is ChartOfAccounts.Skr42;
}
