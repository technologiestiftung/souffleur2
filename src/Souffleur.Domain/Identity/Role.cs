using Microsoft.AspNetCore.Identity;

namespace Souffleur.Domain.Identity;

public class Role : IdentityRole<Guid>
{
    public string? Description { get; set; }

    /// <summary>
    /// Eine Rolle, die souffleur mitbringt. Sie laesst sich aendern, aber
    /// nicht loeschen -- ein Haus ohne Administrator koennte sich selbst
    /// nicht mehr aufschliessen.
    /// </summary>
    public bool IsBuiltIn { get; set; }

    public List<RolePermission> Permissions { get; set; } = [];
}

/// <summary>
/// Die Rollen, die souffleur beim ersten Start anlegt. Feingranulare Rechte
/// kommen mit den Fachmodulen dazu.
/// </summary>
public static class Roles
{
    /// <summary>Verwaltet Konten, Stammdaten und Zugaenge zu Fremdsystemen.</summary>
    public const string Administrator = "Administrator";

    /// <summary>Geschaeftsfuehrung: sieht alles, auch Personal- und Finanzzahlen.</summary>
    public const string Management = "Management";

    /// <summary>Bucht, kontiert und bereitet Zahlungen vor.</summary>
    public const string Accounting = "Accounting";

    /// <summary>Plant Dienste und pflegt Zeiten.</summary>
    public const string Scheduling = "Scheduling";

    /// <summary>Normale Mitarbeit: eigene Zeiten, Aufgaben, Chat.</summary>
    public const string Staff = "Staff";

    public static readonly string[] All =
        [Administrator, Management, Accounting, Scheduling, Staff];

    /// <summary>
    /// Wie eine mitgelieferte Rolle in der Oberflaeche heisst.
    /// </summary>
    /// <remarks>
    /// Der Name in der Datenbank bleibt der englische Schluessel und ist fuer
    /// mitgelieferte Rollen nicht aenderbar. Sonst legt die Einrichtung beim
    /// naechsten Start, die nach dem Namen sucht, eine zweite
    /// „Administrator"-Rolle mit allen Rechten an -- neben der umbenannten.
    /// </remarks>
    public static string Label(string? name) => name switch
    {
        Administrator => "Administration",
        Management => "Geschäftsführung",
        Accounting => "Buchhaltung",
        Scheduling => "Disposition",
        Staff => "Mitarbeit",
        _ => name ?? string.Empty,
    };

    public static string? DefaultDescription(string name) => name switch
    {
        Administrator => "Verwaltet Konten, Stammdaten und Zugänge zu Fremdsystemen.",
        Management => "Sieht alles, auch Personal- und Finanzzahlen.",
        Accounting => "Bucht, kontiert und bereitet Zahlungen vor.",
        Scheduling => "Plant Dienste und Termine.",
        Staff => "Eigene Zeiten, Aufgaben, Chat.",
        _ => null,
    };

    /// <summary>
    /// Womit die mitgelieferten Rollen starten.
    /// </summary>
    /// <remarks>
    /// Genau das, was vorher fest im Quelltext stand -- beim Umstellen soll
    /// sich fuer niemanden etwas aendern. Wer danach mehr oder weniger will,
    /// aendert die Rolle im Haus.
    ///
    /// Der Administrator bekommt alles. Nicht aus Bequemlichkeit: Er ist der,
    /// der die Rechte vergibt, und ein Haus, das sich selbst aussperrt, ruft
    /// sonst uns an.
    /// </remarks>
    public static IReadOnlyDictionary<string, Permission[]> DefaultPermissions { get; } =
        new Dictionary<string, Permission[]>
        {
            [Administrator] = Permissions.All,

            [Management] =
            [
                Permission.HouseManage,
                Permission.ModulesManage,
                Permission.ContactsWrite,
                Permission.FinanceRead,
                Permission.FinanceBook,
                Permission.FinanceExport,
                Permission.PersonnelPlan,
                Permission.PersonnelLevy,
                Permission.PersonnelApprove,
                Permission.PersonnelRecords,
                Permission.CalendarPlan,
                Permission.TicketingManage,
                Permission.DocumentsRead,
                Permission.DocumentsWrite,
                Permission.DocumentsManage,
                Permission.TouringManage,
                Permission.ProductionsManage,
            ],

            [Accounting] =
            [
                Permission.ContactsWrite,
                Permission.FinanceRead,
                Permission.FinanceBook,
                Permission.FinanceExport,
                Permission.PersonnelLevy,
                Permission.DocumentsRead,
                Permission.DocumentsWrite,
            ],

            // Die Disposition plant auch die Reisen: Wer die Abende im Haus
            // verteilt, verteilt auch die Abende anderswo.
            [Scheduling] =
            [
                Permission.ContactsWrite,
                Permission.PersonnelPlan,
                Permission.CalendarPlan,
                Permission.DocumentsRead,
                Permission.DocumentsWrite,
                Permission.TouringManage,
                Permission.ProductionsManage,
            ],

            // Mitarbeit hatte bisher kein einziges der geprueften Rechte --
            // alles, was sie tut (eigene Zeiten, Aufgaben, Chat), verlangt
            // nur die Anmeldung. Dokumente lesen kommt dazu, sonst waere das
            // neue Modul fuer die meisten im Haus leer.
            [Staff] = [Permission.DocumentsRead],
        };
}
