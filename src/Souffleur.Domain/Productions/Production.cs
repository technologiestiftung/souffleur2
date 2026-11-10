using Souffleur.Domain.Catalogs;
using Souffleur.Domain.Common;
using Souffleur.Domain.Contacts;

namespace Souffleur.Domain.Productions;

/// <summary>Wo eine Produktion in ihrem Leben steht.</summary>
public enum ProductionStatus
{
    /// <summary>Beschlossen, noch nicht geprobt.</summary>
    Planned = 1,

    /// <summary>Die Proben laufen, die Premiere steht bevor.</summary>
    Rehearsing = 2,

    /// <summary>Im Spielplan -- wird gezeigt oder reist.</summary>
    Running = 3,

    /// <summary>Abgespielt. Bleibt stehen, weil Belege und Dokumente daran haengen.</summary>
    Archived = 4,
}

public static class ProductionStatuses
{
    public static readonly ProductionStatus[] All = Enum.GetValues<ProductionStatus>();

    public static string Label(this ProductionStatus status) => status switch
    {
        ProductionStatus.Planned => "In Planung",
        ProductionStatus.Rehearsing => "In Probe",
        ProductionStatus.Running => "Im Spielplan",
        ProductionStatus.Archived => "Abgespielt",
        _ => status.ToString(),
    };

    /// <summary>Was das Haus gerade beschaeftigt -- alles ausser dem Abgespielten.</summary>
    public static bool IsCurrent(this ProductionStatus status) =>
        status is not ProductionStatus.Archived;
}

/// <summary>Was eine Produktion braucht: Dinge oder Papiere.</summary>
public enum ProductionNeedKind
{
    /// <summary>Requisite, Kostuem, Technik, Material -- was beschafft oder gebaut wird.</summary>
    Material = 1,

    /// <summary>Vertrag, Textbuch, Pressemappe -- was geschrieben oder eingeholt wird.</summary>
    Document = 2,
}

/// <summary>
/// Eine Produktion: ein Stueck, ein Projekt, ein Abend -- die Klammer, in
/// der das Haus denkt.
/// </summary>
/// <remarks>
/// Die Produktion traegt zweierlei. Erstens ihre Vorgaben: Welche Rollen
/// sind zu besetzen, was darf sie kosten und wofuer, was wird gebraucht,
/// welche Papiere muessen her, wo steht die Recherche. Links haengen als
/// Lesezeichen daran wie an jedem anderen Gegenstand (<c>BookmarkTarget.Production</c>). Das wird hier
/// gefuehrt, weil es nirgends sonst hingehoert -- eine unbesetzte Rolle ist
/// kein Kontakt, eine Budgetposition kein Beleg. Zweitens ist sie der Punkt,
/// an dem Aufgaben, Termine, Belege, Dokumente und Tourneen aus den anderen
/// Modulen zusammenlaufen: Jedes davon traegt einen Verweis auf die
/// Produktion, und die Uebersicht liest ihn. Geloescht wird eine Produktion
/// deshalb nur, wenn nichts mehr an ihr haengt -- sonst wird sie abgespielt
/// und bleibt stehen (ADR-0011).
///
/// Vorher war die Produktion ein Katalogeintrag mit Namen (ADR-0005), weil
/// Touring nur einen Namen brauchte.
/// </remarks>
public class Production : Entity
{
    public required string Title { get; set; }

    /// <summary>Autorin, Komponist, Untertitel -- was auf dem Plakat unter dem Titel steht.</summary>
    public string? Subtitle { get; set; }

    public ProductionStatus Status { get; set; } = ProductionStatus.Planned;

    public DateOnly? PremiereDate { get; set; }

    /// <summary>Die Derniere -- danach ist die Produktion abgespielt.</summary>
    public DateOnly? ClosingDate { get; set; }

    /// <summary>
    /// Das Budget: was die Produktion insgesamt kosten darf, brutto. Die
    /// Budgetpositionen verteilen ihn, die Ist-Kosten kommen aus den Belegen.
    /// </summary>
    public decimal? Budget { get; set; }

    /// <summary>Worum es geht -- die Produktionsbeschreibung, wie sie auch nach aussen gehen koennte.</summary>
    public string? Description { get; set; }

    /// <summary>Was zum Stueck zusammengetragen wurde: Quellen, Fassungen, Hintergruende.</summary>
    public string? Research { get; set; }

    /// <summary>Interne Notizen, die weder Beschreibung noch Recherche sind.</summary>
    public string? Notes { get; set; }

    /// <summary>Die Rollen und Funktionen der Produktion -- besetzt oder noch offen.</summary>
    public List<ProductionRole> Roles { get; set; } = [];

    /// <summary>Wofuer das Budget gedacht ist, Position fuer Position.</summary>
    public List<ProductionBudgetLine> BudgetLines { get; set; } = [];

    /// <summary>Was gebraucht wird: Dinge und Papiere.</summary>
    public List<ProductionNeed> Needs { get; set; } = [];

    /// <summary>Was die Positionen zusammen vorsehen -- gegen das Budget zu lesen.</summary>
    public decimal PlannedTotal => BudgetLines.Sum(l => l.Amount);

    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];

        if (string.IsNullOrWhiteSpace(Title))
        {
            problems.Add("Eine Produktion braucht einen Titel.");
        }

        if (PremiereDate is { } premiere && ClosingDate is { } closing && closing < premiere)
        {
            problems.Add("Die Derniere liegt vor der Premiere.");
        }

        if (Budget is < 0)
        {
            problems.Add("Ein Budget ist nicht negativ.");
        }

        if (Roles.Any(r => string.IsNullOrWhiteSpace(r.Name)))
        {
            problems.Add("Jede Rolle braucht einen Namen.");
        }

        if (BudgetLines.Any(l => string.IsNullOrWhiteSpace(l.Title)))
        {
            problems.Add("Jede Budgetposition braucht eine Bezeichnung.");
        }

        if (BudgetLines.Any(l => l.Amount < 0))
        {
            problems.Add("Eine Budgetposition ist nicht negativ.");
        }

        if (Needs.Any(n => string.IsNullOrWhiteSpace(n.Title)))
        {
            problems.Add("Jeder Bedarf braucht eine Bezeichnung.");
        }

        return problems;
    }
}

/// <summary>
/// Eine Rolle oder Funktion in der Produktion: Hamlet, Ophelia, Regie,
/// Buehnenbild, Abendspielleitung. Besetzt, sobald jemand aus dem
/// Adressbuch darauf steht -- sonst ist sie offen, und das ist eine
/// Information, keine Luecke.
/// </summary>
/// <remarks>
/// Dieselbe Person darf mehrere Rollen haben (Doppelbesetzung, Regie und
/// Buehne in einer Hand); dieselbe Rolle kann in zwei Zeilen stehen, wenn
/// sie doppelt besetzt ist. Die Funktion aus dem Katalog „Funktionen am
/// Termin" ordnet die Rolle einem Bereich zu -- Ensemble, Regie, Technik.
/// </remarks>
public class ProductionRole : Entity
{
    public Guid ProductionId { get; set; }

    public Production Production { get; set; } = null!;

    /// <summary>Wie die Rolle heisst -- im Besetzungszettel oder im Stab.</summary>
    public required string Name { get; set; }

    /// <summary>Welcher Bereich -- Katalog „Funktionen am Termin".</summary>
    public Guid? FunctionId { get; set; }

    public CatalogEntry? Function { get; set; }

    /// <summary>Wer die Rolle hat. Leer heisst: noch zu besetzen.</summary>
    public Guid? ContactId { get; set; }

    public Contact? Contact { get; set; }

    public string? Note { get; set; }

    public int SortOrder { get; set; }

    public bool IsCast => ContactId is not null;
}

/// <summary>
/// Eine Position des Budgets: Gagen, Ausstattung, Technik, Werbung -- was die
/// Produktion wofuer ausgeben darf. Plan, nicht Ist; das Ist steht in den
/// Belegen.
/// </summary>
public class ProductionBudgetLine : Entity
{
    public Guid ProductionId { get; set; }

    public Production Production { get; set; } = null!;

    public required string Title { get; set; }

    /// <summary>Gagen, Ausstattung, Technik, Werbung, Reise -- frei, aber mit Vorschlaegen.</summary>
    public string? Category { get; set; }

    /// <summary>Brutto, in Euro.</summary>
    public decimal Amount { get; set; }

    public string? Note { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>
/// Etwas, das die Produktion braucht: ein Ding oder ein Papier. Abgehakt,
/// sobald es da ist. Was beschafft werden muss, wird hier aufgeschrieben;
/// wer es beschafft, steht in einer Aufgabe.
/// </summary>
public class ProductionNeed : Entity
{
    public Guid ProductionId { get; set; }

    public Production Production { get; set; } = null!;

    public ProductionNeedKind Kind { get; set; } = ProductionNeedKind.Material;

    public required string Title { get; set; }

    /// <summary>Wie viel, wie gross, welche Fassung -- frei formuliert.</summary>
    public string? Quantity { get; set; }

    public bool IsDone { get; set; }

    public string? Note { get; set; }

    public int SortOrder { get; set; }
}
