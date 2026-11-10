using Souffleur.Domain.Catalogs;
using Souffleur.Domain.Common;
using Souffleur.Domain.Identity;

namespace Souffleur.Domain.Personnel;

/// <summary>
/// Wer bei einer Terminart gebraucht wird.
/// </summary>
/// <remarks>
/// Der Bedarf haengt an der Terminkategorie und nicht an einer eigenen
/// Schichtliste: Dienste entstehen im Theater an Vorstellungen und Proben, und
/// die stehen schon im Kalender. Eine zweite Zeitachse daneben waere eine
/// zweite Wahrheit darueber, was an einem Abend stattfindet.
///
/// Je Terminart ein Bedarf. Zwei Vorlagen fuer dieselbe Kategorie muessten
/// gegeneinander abgewogen werden, und die Regel dafuer koennte niemand im
/// Kopf behalten.
/// </remarks>
public class DutyTemplate : Entity
{
    public required Guid CategoryId { get; set; }

    public CatalogEntry? Category { get; set; }

    /// <summary>Warum der Bedarf so aussieht -- steht in der Verwaltung dabei.</summary>
    public string? Note { get; set; }

    public List<DutyRequirement> Requirements { get; set; } = [];

    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];

        if (Requirements.Count == 0)
        {
            problems.Add("Ein Bedarf ohne Funktionen sagt nichts aus.");
        }

        if (Requirements.Select(r => r.FunctionId).Distinct().Count() != Requirements.Count)
        {
            problems.Add("Dieselbe Funktion steht zweimal darin.");
        }

        foreach (DutyRequirement requirement in Requirements)
        {
            if (requirement.Count is < 1 or > 50)
            {
                problems.Add("Eine Anzahl liegt zwischen 1 und 50.");
                break;
            }
        }

        return problems;
    }
}

/// <summary>Eine Funktion und wie oft sie gebraucht wird.</summary>
public class DutyRequirement : Entity
{
    public Guid TemplateId { get; set; }

    public DutyTemplate Template { get; set; } = null!;

    /// <summary>Derselbe Katalog wie am Termin -- „Technik" heisst hier dasselbe.</summary>
    public required Guid FunctionId { get; set; }

    public CatalogEntry? Function { get; set; }

    public required int Count { get; set; }

    /// <summary>
    /// Wer dafuer infrage kommt -- „Beleuchtung" aus der Gruppe Technik.
    /// </summary>
    /// <remarks>
    /// Ein Vorschlag, keine Sperre. Beim Besetzen stehen zuerst die Leute
    /// der Gruppe zur Wahl; wer jemanden ausserhalb einteilen will, kann es.
    /// Am Abend vor der Premiere ist die Einspringerin aus der Requisite
    /// besser als eine leere Stelle.
    /// </remarks>
    public Guid? GroupId { get; set; }

    public Group? Group { get; set; }
}

/// <summary>
/// Eine Funktion an einem Termin: wie viele gebraucht werden, wie viele
/// eingeteilt sind.
/// </summary>
public record DutySlot(
    Guid FunctionId,
    string Function,
    int Required,
    int Assigned,
    Guid? GroupId = null,
    string? GroupName = null)
{
    /// <summary>Was fehlt. Mehr Eingeteilte als noetig sind kein Fehler.</summary>
    public int Open => Math.Max(0, Required - Assigned);
}

/// <summary>Wer an einem Termin eingeteilt ist, wie er im Dienstplan erscheint.</summary>
public record DutyPerson(
    Guid ContactId,
    string Name,
    string? Function,
    string StatusLabel,
    bool Declined);

/// <summary>Ein Termin mit seinem Bedarf und seiner Einteilung.</summary>
public record PlannedDuty(
    Guid? EventId,
    DateTimeOffset StartsAt,
    DateOnly LocalDate,
    string Title,
    string? Location,
    Guid? CategoryId,
    string? CategoryLabel,
    IReadOnlyList<DutySlot> Slots,
    IReadOnlyList<DutyPerson> Assigned)
{
    public int Open => Slots.Sum(slot => slot.Open);
}
