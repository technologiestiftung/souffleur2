using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Calendar;
using Souffleur.Domain.Personnel;
using Souffleur.Infrastructure.Calendar;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Personnel;

public class DutyPlanException(IReadOnlyList<string> problems)
    : Exception(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;

    public DutyPlanException(string problem) : this([problem])
    {
    }
}

/// <summary>
/// Der Dienstplan: was an einem Termin gebraucht wird, und wer schon
/// eingeteilt ist.
/// </summary>
/// <remarks>
/// Gerechnet wird gegen den Kalender, nicht gegen eine eigene Schichtliste.
/// Der Gewinn daran ist die Luecke: Eine Vorstellung ohne jede Einteilung
/// taucht im Dienstplan auf, weil ihre Terminart einen Bedarf hat -- in einer
/// Liste der Eingeteilten waere sie unsichtbar.
/// </remarks>
public class DutyPlanService(SouffleurDbContext context, CalendarService calendar)
{
    public Task<List<DutyTemplate>> TemplatesAsync(CancellationToken token = default) =>
        context.DutyTemplates
            .Include(t => t.Category)
            .Include(t => t.Requirements).ThenInclude(r => r.Function)
            .Include(t => t.Requirements).ThenInclude(r => r.Group)
            .AsNoTracking()
            .OrderBy(t => t.Category!.SortOrder)
            .ToListAsync(token);

    /// <summary>
    /// Den Bedarf einer Terminart setzen. Ersetzt den vorhandenen -- je
    /// Terminart gibt es genau einen.
    /// </summary>
    public Task<DutyTemplate> SaveAsync(
        Guid categoryId,
        IReadOnlyCollection<(Guid FunctionId, int Count)> requirements,
        string? note = null,
        CancellationToken token = default) =>
        SaveWithGroupsAsync(
            categoryId,
            [.. requirements.Select(r => (r.FunctionId, r.Count, (Guid?)null))],
            note,
            token);

    /// <summary>
    /// Wie <see cref="SaveAsync"/>, mit der Gruppe, aus der vorgeschlagen
    /// wird. Eigener Name, weil eine leere Liste sonst zu beiden passt.
    /// </summary>
    public async Task<DutyTemplate> SaveWithGroupsAsync(
        Guid categoryId,
        IReadOnlyCollection<(Guid FunctionId, int Count, Guid? GroupId)> requirements,
        string? note = null,
        CancellationToken token = default)
    {
        List<DutyRequirement> fresh =
        [
            .. requirements.Select(r => new DutyRequirement
            {
                FunctionId = r.FunctionId,
                Count = r.Count,
                GroupId = r.GroupId,
            }),
        ];

        // Geprueft wird, bevor etwas angefasst wird -- an einem Entwurf, den
        // der Kontext nie zu sehen bekommt.
        IReadOnlyList<string> problems =
            new DutyTemplate { CategoryId = categoryId, Requirements = fresh }.Validate();

        if (problems.Count > 0)
        {
            throw new DutyPlanException(problems);
        }

        DutyTemplate? template = await context.DutyTemplates
            .FirstOrDefaultAsync(t => t.CategoryId == categoryId, token);

        if (template is null)
        {
            context.DutyTemplates.Add(new DutyTemplate
            {
                CategoryId = categoryId,
                Note = Trimmed(note),
                Requirements = fresh,
            });

            await context.SaveChangesAsync(token);
            return await LoadAsync(categoryId, token);
        }

        template.Note = Trimmed(note);

        // Erst die alten Zeilen weg und gespeichert, dann die neuen. In einem
        // Zug waere dieselbe Kombination aus Bedarf und Funktion kurzzeitig
        // zweimal da -- und darauf liegt ein eindeutiger Index.
        List<DutyRequirement> previous = await context.DutyRequirements
            .Where(r => r.TemplateId == template.Id)
            .ToListAsync(token);

        context.DutyRequirements.RemoveRange(previous);
        await context.SaveChangesAsync(token);

        foreach (DutyRequirement requirement in fresh)
        {
            requirement.TemplateId = template.Id;
        }

        context.DutyRequirements.AddRange(fresh);
        await context.SaveChangesAsync(token);

        return await LoadAsync(categoryId, token);
    }

    private async Task<DutyTemplate> LoadAsync(Guid categoryId, CancellationToken token) =>
        await context.DutyTemplates
            .Include(t => t.Category)
            .Include(t => t.Requirements).ThenInclude(r => r.Function)
            .Include(t => t.Requirements).ThenInclude(r => r.Group)
            .AsNoTracking()
            .FirstAsync(t => t.CategoryId == categoryId, token);

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public async Task RemoveAsync(Guid categoryId, CancellationToken token = default)
    {
        DutyTemplate? template = await context.DutyTemplates
            .Include(t => t.Requirements)
            .FirstOrDefaultAsync(t => t.CategoryId == categoryId, token);

        if (template is null)
        {
            return;
        }

        // Die Anforderungen haengen am Bedarf und gehen mit ihm (Cascade).
        context.DutyTemplates.Remove(template);
        await context.SaveChangesAsync(token);
    }

    /// <summary>
    /// Der Plan eines Zeitraums: jeder Termin, der einen Bedarf hat oder eine
    /// Einteilung traegt.
    /// </summary>
    public async Task<List<PlannedDuty>> PlanAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken token = default)
    {
        List<DutyTemplate> templates = await TemplatesAsync(token);

        Dictionary<Guid, List<DutyRequirement>> byCategory = templates
            .ToDictionary(t => t.CategoryId, t => t.Requirements);

        List<CalendarOccurrence> occurrences =
            await calendar.RangeAsync(from, to, includeCancelled: false, token: token);

        List<PlannedDuty> plan = [];

        foreach (CalendarOccurrence occurrence in occurrences)
        {
            // Fristen aus den Aufgaben stehen im Kalender, sind aber keine
            // Dienste -- sie tragen „Zuständig" als Funktion und wuerden die
            // Zaehlung verfaelschen.
            if (occurrence.Source != CalendarSource.Calendar)
            {
                continue;
            }

            List<DutyRequirement> required =
                occurrence.CategoryId is { } category
                && byCategory.TryGetValue(category, out List<DutyRequirement>? found)
                    ? found
                    : [];

            // Wer abgesagt hat, besetzt nichts.
            var assigned = occurrence.Participants
                .Where(p => p.Status.Occupies() && p.FunctionId is not null)
                .GroupBy(p => p.FunctionId!.Value)
                .ToDictionary(group => group.Key, group => group.Count());

            List<DutySlot> slots =
            [
                .. required.Select(requirement => new DutySlot(
                    requirement.FunctionId,
                    requirement.Function?.Label ?? "—",
                    requirement.Count,
                    assigned.GetValueOrDefault(requirement.FunctionId),
                    requirement.GroupId,
                    requirement.Group?.Name)),
            ];

            if (slots.Count == 0 && occurrence.Participants.Count == 0)
            {
                continue;
            }

            plan.Add(new PlannedDuty(
                occurrence.EventId,
                occurrence.StartsAt,
                occurrence.LocalDate,
                occurrence.Title,
                occurrence.Location,
                occurrence.CategoryId,
                occurrence.CategoryLabel,
                slots,
                [
                    .. occurrence.Participants.Select(p => new DutyPerson(
                        p.ContactId,
                        p.Name,
                        p.Function,
                        p.StatusLabel,
                        p.Status == ParticipationStatus.Declined)),
                ]));
        }

        return plan;
    }
}
