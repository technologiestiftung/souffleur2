using Souffleur.Domain.Common;

namespace Souffleur.Domain.Contacts;

/// <summary>
/// Wer mit dem Haus zu tun hat -- die eine Stelle, an der das steht.
/// </summary>
/// <remarks>
/// Kontakte sind kein Modul neben den anderen, sondern die Grundlage unter
/// ihnen: Die Buchhaltung bucht gegen sie, Belege verweisen auf sie, Aufgaben
/// haengen an ihnen, der Dienstplan kennt die Leute dahinter. Deshalb gibt es
/// **ein** Adressbuch und nicht eines je Modul -- sonst pflegt man jeden Umzug
/// dreimal und keine Anschrift stimmt.
///
/// Welche Rolle jemand spielt, steht in <see cref="Roles"/>, und zwar mehrfach:
/// Die Druckerei kann Lieferant sein, die Geschaeftsfuehrerin derselben
/// Druckerei zugleich Ansprechpartnerin im Presseverteiler.
/// </remarks>
public class Contact : Entity
{
    public required ContactKind Kind { get; set; }

    /// <summary>
    /// Anzeigename: bei einer Organisation ihr Name, bei einer Person
    /// "Nachname, Vorname". Wird beim Speichern gesetzt, nicht eingegeben.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>Nur bei Personen besetzt.</summary>
    public string? GivenName { get; set; }

    public string? Surname { get; set; }

    /// <summary>Anrede oder Titel, wie die Person angeschrieben werden will.</summary>
    public string? FormOfAddress { get; set; }

    /// <summary>Funktion im Haus oder in der Organisation.</summary>
    public string? Position { get; set; }

    /// <summary>Bei einer Person: die Organisation, für die sie auftritt.</summary>
    public Guid? OrganizationContactId { get; set; }

    public Contact? OrganizationContact { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Website { get; set; }

    public string? Street { get; set; }

    public string? PostalCode { get; set; }

    public string? City { get; set; }

    public string? Country { get; set; }

    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Das Benutzerkonto dieser Person, falls sie eines hat.
    /// </summary>
    /// <remarks>
    /// Die Rolle <c>Haus</c> sagt, dass jemand zum Haus gehoert; hier steht,
    /// unter welchem Konto er sich anmeldet. Ohne diese Verbindung waere
    /// derselbe Mensch zweimal im System: einmal als Kontakt, der Dienst hat,
    /// einmal als Konto, das stempelt -- und die eigene Zeit waere nicht die
    /// eigene.
    ///
    /// Nicht jeder Kontakt hat ein Konto, und nicht jedes Konto muss einem
    /// Kontakt gehoeren. Aber ein Konto gehoert hoechstens einem.
    /// </remarks>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Woher die Daten stammen -- selbst erfasst, aus einem Beleg, aus einer
    /// Visitenkarte, von der Website. Die Datenschutz-Grundverordnung verlangt,
    /// dass das Haus die Herkunft benennen kann (Art. 14 DSGVO).
    /// </summary>
    public string? Origin { get; set; }

    /// <summary>
    /// Wann jemand zugestimmt hat, angeschrieben zu werden. Nur gesetzt, wo eine
    /// Einwilligung noetig ist -- bei einem Lieferanten braucht es keine, bei
    /// einem Presseverteiler schon.
    /// </summary>
    public DateTimeOffset? ContactConsentAt { get; set; }

    /// <summary>Widerruf der Einwilligung. Gesetzt heisst: nicht mehr anschreiben.</summary>
    public DateTimeOffset? ContactConsentWithdrawnAt { get; set; }

    public List<ContactRoleAssignment> Roles { get; set; } = [];

    /// <summary>Bankverbindung und Steuerangaben. Nur besetzt, wo gebucht wird.</summary>
    public ContactBankDetails? BankDetails { get; set; }

    public bool MayBeContacted =>
        ContactConsentWithdrawnAt is null
        && (ContactConsentAt is not null || !Roles.Any(r => r.Role.NeedsConsent()));

    public bool HasRole(ContactRole role) => Roles.Any(r => r.Role == role);

    /// <summary>
    /// Baut den Anzeigenamen aus dem, was da ist. Bei Personen nachgestellt,
    /// damit Listen nach Nachnamen sortieren.
    /// </summary>
    public void UpdateName(string? organizationName = null)
    {
        if (Kind == ContactKind.Person)
        {
            string surname = Surname?.Trim() ?? string.Empty;
            string given = GivenName?.Trim() ?? string.Empty;

            Name = (surname, given) switch
            {
                ("", "") => organizationName ?? Name,
                ("", _) => given,
                (_, "") => surname,
                _ => $"{surname}, {given}",
            };
        }
    }

    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];

        if (Kind == ContactKind.Person && string.IsNullOrWhiteSpace(Surname))
        {
            problems.Add("Eine Person braucht einen Nachnamen.");
        }

        if (Kind == ContactKind.Organization && string.IsNullOrWhiteSpace(Name))
        {
            problems.Add("Eine Organisation braucht einen Namen.");
        }

        if (Email is { Length: > 0 } email && !email.Contains('@'))
        {
            problems.Add($"„{email}“ sieht nicht nach einer E-Mail-Adresse aus.");
        }

        if (Roles.Count == 0)
        {
            problems.Add("Ein Kontakt braucht mindestens eine Rolle.");
        }

        if (OrganizationContactId is not null && Kind == ContactKind.Organization)
        {
            problems.Add("Eine Organisation gehört nicht zu einer anderen Organisation.");
        }

        problems.AddRange(BankDetails?.Validate() ?? []);

        return problems;
    }
}

public enum ContactKind
{
    Organization = 1,
    Person = 2,
}
