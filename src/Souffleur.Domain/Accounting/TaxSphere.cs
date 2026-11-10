namespace Souffleur.Domain.Accounting;

/// <summary>
/// Die vier Taetigkeitsbereiche, nach denen eine gemeinnuetzige Koerperschaft
/// ihre Einnahmen und Ausgaben trennen muss.
/// </summary>
/// <remarks>
/// Das Finanzamt verlangt die Trennung, weil an ihr zwei Dinge haengen: ob ein
/// Ueberschuss koerperschaftsteuerpflichtig ist, und welcher Umsatzsteuersatz
/// gilt. Im Zweckbetrieb sind es in der Regel 7 Prozent, im wirtschaftlichen
/// Geschaeftsbetrieb 19.
///
/// Fuer ein Theater heisst das konkret: Der Kartenverkauf zur eigenen Auffuehrung
/// ist Zweckbetrieb, der Getraenkeverkauf im Foyer wirtschaftlicher
/// Geschaeftsbetrieb, die vermietete Lagerflaeche Vermoegensverwaltung, der
/// Mitgliedsbeitrag ideeller Bereich.
///
/// Die Zuordnung im Einzelfall gehoert dem Steuerbuero, nicht souffleur. souffleur
/// sorgt dafuer, dass sie erfasst wird und nicht verlorengeht.
/// </remarks>
public enum TaxSphere
{
    /// <summary>
    /// Ideeller Bereich: Mitgliedsbeitraege, Spenden, echte Zuschuesse. Keine
    /// Gegenleistung, keine Steuer.
    /// </summary>
    Ideal = 1,

    /// <summary>
    /// Vermoegensverwaltung: Zinsen, Mieten aus langfristiger Vermietung,
    /// Verpachtung.
    /// </summary>
    AssetManagement = 2,

    /// <summary>
    /// Zweckbetrieb nach §§ 65 ff. AO: die satzungsgemaesse Taetigkeit selbst --
    /// bei einem Theater die eigenen Auffuehrungen.
    /// </summary>
    PurposeOperation = 3,

    /// <summary>
    /// Wirtschaftlicher Geschaeftsbetrieb nach § 14 AO: Gastronomie, Werbung,
    /// Verkauf. Steuerpflichtig, sobald die Freigrenze ueberschritten ist.
    /// </summary>
    CommercialOperation = 4,
}

public static class TaxSpheres
{
    public static readonly TaxSphere[] All = Enum.GetValues<TaxSphere>();

    public static string Label(this TaxSphere sphere) => sphere switch
    {
        TaxSphere.Ideal => "Ideeller Bereich",
        TaxSphere.AssetManagement => "Vermögensverwaltung",
        TaxSphere.PurposeOperation => "Zweckbetrieb",
        TaxSphere.CommercialOperation => "Wirtschaftlicher Geschäftsbetrieb",
        _ => sphere.ToString(),
    };

    /// <summary>Beispiel aus dem Theaterbetrieb, fuer die Auswahl in der Oberflaeche.</summary>
    public static string Example(this TaxSphere sphere) => sphere switch
    {
        TaxSphere.Ideal => "Mitgliedsbeiträge, Spenden, echte Zuschüsse",
        TaxSphere.AssetManagement => "Zinsen, langfristig vermietete Flächen",
        TaxSphere.PurposeOperation => "Eintritt zu eigenen Aufführungen",
        TaxSphere.CommercialOperation => "Foyergastronomie, Werbung, Programmheftanzeigen",
        _ => string.Empty,
    };
}
