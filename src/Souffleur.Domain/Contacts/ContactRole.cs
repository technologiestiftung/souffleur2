using Souffleur.Domain.Common;

namespace Souffleur.Domain.Contacts;

/// <summary>
/// In welcher Eigenschaft jemand mit dem Haus zu tun hat. Mehrere zugleich sind
/// der Normalfall, nicht die Ausnahme.
/// </summary>
public enum ContactRole
{
    /// <summary>Stellt dem Haus Rechnungen. Kreditor in der Buchhaltung.</summary>
    Supplier = 1,

    /// <summary>Bekommt Rechnungen vom Haus. Debitor in der Buchhaltung.</summary>
    Customer = 2,

    /// <summary>Gibt Geld: Senat, Stiftung, Fonds, Sponsor.</summary>
    Funder = 3,

    /// <summary>Presse und Medien.</summary>
    Press = 4,

    /// <summary>Arbeitet kuenstlerisch mit: Regie, Ensemble, Ausstattung, Musik.</summary>
    Artist = 5,

    /// <summary>Kooperationspartner, Koproduzent.</summary>
    Partner = 6,

    /// <summary>Spielstaette auf Gastspiel oder Tournee.</summary>
    Venue = 7,

    /// <summary>Behoerde, Amt, Kammer, Berufsgenossenschaft.</summary>
    Authority = 8,

    /// <summary>Gehoert zum Haus. Verbindet den Kontakt mit einem Benutzerkonto.</summary>
    Staff = 9,
}

public static class ContactRoles
{
    public static readonly ContactRole[] All = Enum.GetValues<ContactRole>();

    public static string Label(this ContactRole role) => role switch
    {
        ContactRole.Supplier => "Lieferant",
        ContactRole.Customer => "Kunde",
        ContactRole.Funder => "Förderer",
        ContactRole.Press => "Presse",
        ContactRole.Artist => "Künstlerisch",
        ContactRole.Partner => "Partner",
        ContactRole.Venue => "Spielstätte",
        ContactRole.Authority => "Behörde",
        ContactRole.Staff => "Haus",
        _ => role.ToString(),
    };

    /// <summary>
    /// Ob diese Rolle eine Einwilligung braucht, bevor das Haus schreibt.
    /// </summary>
    /// <remarks>
    /// Eine Rechnung an einen Lieferanten ist Vertragserfuellung und braucht
    /// keine. Ein Presseverteiler ist Werbung und braucht eine (Art. 6 DSGVO,
    /// § 7 UWG). Die Einordnung im Einzelfall gehoert ins Haus; souffleur sorgt
    /// dafuer, dass die Frage gestellt wird.
    /// </remarks>
    public static bool NeedsConsent(this ContactRole role) =>
        role is ContactRole.Press;

    /// <summary>Ob diese Rolle Bankdaten und ein Personenkonto braucht.</summary>
    public static bool NeedsBankDetails(this ContactRole role) =>
        role is ContactRole.Supplier or ContactRole.Customer or ContactRole.Artist;
}

/// <summary>
/// Eine Rolle an einem Kontakt. Eigene Zeile, damit sich abfragen laesst,
/// wer Foerderer ist, ohne jeden Kontakt zu lesen.
/// </summary>
public class ContactRoleAssignment : Entity
{
    public Guid ContactId { get; set; }

    public Contact Contact { get; set; } = null!;

    public required ContactRole Role { get; set; }

    /// <summary>Was diese Rolle im Haus bedeutet, in eigenen Worten.</summary>
    public string? Note { get; set; }
}
