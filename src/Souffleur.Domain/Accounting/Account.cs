using Souffleur.Domain.Common;

namespace Souffleur.Domain.Accounting;

/// <summary>
/// Ein Sachkonto aus dem Kontenrahmen des Hauses.
/// </summary>
/// <remarks>
/// Der Kontenrahmen wird nicht mitgeliefert: Welche Konten gelten, sagt das
/// Steuerbuero des Hauses (SKR03 oder SKR04 als Vorlage). Erfundene Konten waeren
/// schlimmer als keine, weil sie beim Export im Buchungsstapel landen.
/// </remarks>
public class Account : Entity
{
    /// <summary>Kontonummer, wie das Steuerbuero sie fuehrt -- vierstellig im SKR03.</summary>
    public required string Number { get; set; }

    public required string Name { get; set; }

    public required AccountType Type { get; set; }

    /// <summary>
    /// Steuerschluessel des Kontenrahmens, falls das Konto einen festen traegt.
    /// Leer heisst: Der Schluessel wird je Buchungszeile angegeben.
    /// </summary>
    public string? TaxKey { get; set; }

    /// <summary>
    /// Sphaere, die souffleur bei einer Buchung auf dieses Konto vorschlaegt.
    /// Ein Erloeskonto fuer Eintritte gehoert in den Zweckbetrieb, eines fuer
    /// Getraenke in den wirtschaftlichen Geschaeftsbetrieb. Der Vorschlag laesst
    /// sich je Buchung aendern -- entscheiden tut das Steuerbuero.
    /// </summary>
    public TaxSphere? DefaultTaxSphere { get; set; }

    /// <summary>
    /// Stillgelegte Konten bleiben erhalten, weil alte Buchungen darauf verweisen.
    /// Sie lassen sich nur nicht mehr neu bebuchen.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public string Label => $"{Number} {Name}";
}

/// <summary>
/// Die Kontenart entscheidet, auf welcher Seite ein Konto zunimmt und wo es in
/// der Auswertung erscheint.
/// </summary>
public enum AccountType
{
    /// <summary>Anlage- und Umlaufvermoegen, Bank, Kasse. Zunahme im Soll.</summary>
    Asset = 1,

    /// <summary>Verbindlichkeiten und Rueckstellungen. Zunahme im Haben.</summary>
    Liability = 2,

    /// <summary>Eigenkapital, Ruecklagen. Zunahme im Haben.</summary>
    Equity = 3,

    /// <summary>Ertraege: Eintritt, Foerderung, Spenden. Zunahme im Haben.</summary>
    Revenue = 4,

    /// <summary>Aufwendungen: Gagen, Miete, Material. Zunahme im Soll.</summary>
    Expense = 5,

    /// <summary>Personenkonto eines Kunden.</summary>
    Receivable = 6,

    /// <summary>Personenkonto eines Lieferanten.</summary>
    Payable = 7,
}
