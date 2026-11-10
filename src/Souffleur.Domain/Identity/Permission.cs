namespace Souffleur.Domain.Identity;

/// <summary>
/// Was jemand darf. Der Katalog, aus dem Rollen zusammengesetzt werden.
/// </summary>
/// <remarks>
/// Zahlen mit Luecken und nach Bereichen geordnet: Ein neues Recht bekommt
/// eine freie Nummer in seinem Hunderter und stoert die bestehenden nicht.
/// Die Zahl steht in der Datenbank, der Name nur im Quelltext -- umbenennen
/// ist deshalb gefahrlos, umnummerieren nie.
///
/// Der Katalog bildet zunaechst genau das ab, was heute geprueft wird. Wo
/// bisher die blosse Anmeldung genuegte, genuegt sie weiterhin: Ein Recht
/// einzufuehren, das niemand hat, sperrt ein Haus aus seiner eigenen
/// Anwendung aus. Verfeinert wird spaeter und einzeln.
/// </remarks>
public enum Permission
{
    // ── Haus und Zugaenge ────────────────────────────────────────────────
    /// <summary>Stammdaten des Hauses aendern.</summary>
    HouseManage = 100,

    /// <summary>Konten, Rollen und Gruppen verwalten.</summary>
    AccountsManage = 110,

    /// <summary>Module an- und abschalten.</summary>
    ModulesManage = 120,

    /// <summary>Die Listen des Hauses pflegen.</summary>
    CatalogsManage = 130,

    /// <summary>Ressourcen anlegen und aendern.</summary>
    ResourcesManage = 140,

    // ── Kontakte ─────────────────────────────────────────────────────────
    /// <summary>Kontakte anlegen und aendern.</summary>
    ContactsWrite = 200,

    // ── Finanzen ─────────────────────────────────────────────────────────
    /// <summary>Buchungen, Belege und Bank sehen.</summary>
    FinanceRead = 300,

    /// <summary>Buchen, kontieren, Belege erfassen.</summary>
    FinanceBook = 310,

    /// <summary>Stapel fuer die Steuerkanzlei erzeugen.</summary>
    FinanceExport = 320,

    // ── Personal ─────────────────────────────────────────────────────────
    /// <summary>Dienste planen und den Bedarf setzen.</summary>
    PersonnelPlan = 400,

    /// <summary>Die Kuenstlersozialabgabe einsehen und kennzeichnen.</summary>
    PersonnelLevy = 410,

    /// <summary>
    /// Antraege auf Abwesenheit entscheiden und sehen, wer warum fehlt --
    /// auch Krankmeldungen.
    /// </summary>
    PersonnelApprove = 420,

    /// <summary>Vertragsdaten pflegen: Stunden, Arbeitstage, Urlaubsanspruch.</summary>
    PersonnelRecords = 430,

    // ── Kalender ─────────────────────────────────────────────────────────
    /// <summary>Termine anlegen, aendern und besetzen.</summary>
    CalendarPlan = 500,

    // ── Ticketing ────────────────────────────────────────────────────────
    /// <summary>Den Vorverkauf einrichten und pruefen.</summary>
    TicketingManage = 600,

    // ── Dokumente ────────────────────────────────────────────────────────
    /// <summary>Dokumente sehen und herunterladen.</summary>
    DocumentsRead = 700,

    /// <summary>Dokumente ablegen, beschriften und verknuepfen.</summary>
    DocumentsWrite = 710,

    /// <summary>Fremde Dokumente aendern und wegwerfen.</summary>
    DocumentsManage = 720,

    // ── Touring ──────────────────────────────────────────────────────────
    /// <summary>Tourneen und Gastspiele anlegen, aendern und abrechnen.</summary>
    TouringManage = 800,

    // ── Produktionen ─────────────────────────────────────────────────────
    /// <summary>Produktionen anlegen, aendern, besetzen und abspielen.</summary>
    ProductionsManage = 900,
}

public static class Permissions
{
    public static readonly Permission[] All = Enum.GetValues<Permission>();

    /// <summary>Unter welcher Ueberschrift das Recht in der Verwaltung steht.</summary>
    public static string Area(this Permission permission) => (int)permission switch
    {
        < 200 => "Haus und Zugänge",
        < 300 => "Kontakte",
        < 400 => "Finanzen",
        < 500 => "Personal",
        < 600 => "Kalender",
        < 700 => "Ticketing",
        < 800 => "Dokumente",
        < 900 => "Touring",
        _ => "Produktionen",
    };

    public static string Label(this Permission permission) => permission switch
    {
        Permission.HouseManage => "Stammdaten des Hauses ändern",
        Permission.AccountsManage => "Konten, Rollen und Gruppen verwalten",
        Permission.ModulesManage => "Module an- und abschalten",
        Permission.CatalogsManage => "Listen des Hauses pflegen",
        Permission.ResourcesManage => "Ressourcen verwalten",
        Permission.ContactsWrite => "Kontakte anlegen und ändern",
        Permission.FinanceRead => "Buchungen, Belege und Bank sehen",
        Permission.FinanceBook => "Buchen und Belege erfassen",
        Permission.FinanceExport => "Stapel für die Steuerkanzlei erzeugen",
        Permission.PersonnelPlan => "Dienste planen",
        Permission.PersonnelLevy => "Künstlersozialabgabe einsehen",
        Permission.PersonnelApprove => "Abwesenheiten genehmigen, Krankmeldungen sehen",
        Permission.PersonnelRecords => "Vertragsdaten und Urlaubskonten pflegen",
        Permission.CalendarPlan => "Termine planen und besetzen",
        Permission.TicketingManage => "Vorverkauf einrichten",
        Permission.DocumentsRead => "Dokumente sehen",
        Permission.DocumentsWrite => "Dokumente ablegen",
        Permission.DocumentsManage => "Fremde Dokumente ändern und wegwerfen",
        Permission.TouringManage => "Tourneen und Gastspiele planen",
        Permission.ProductionsManage => "Produktionen anlegen und ändern",
        _ => permission.ToString(),
    };
}
