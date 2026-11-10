using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Accounting;
using Souffleur.Domain.Bookmarks;
using Souffleur.Domain.Calendar;
using Souffleur.Domain.Catalogs;
using Souffleur.Domain.Common;
using Souffleur.Domain.Documents;
using Souffleur.Domain.Modules;
using Souffleur.Domain.Productions;
using Souffleur.Domain.Touring;
using Souffleur.Domain.Work;
using Souffleur.Infrastructure.Documents;
using Souffleur.Infrastructure.Modules;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Productions;

public class ProductionException(IReadOnlyList<string> problems)
    : Exception(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;

    public ProductionException(string problem) : this([problem])
    {
    }
}

/// <summary>Eine Rolle, wie sie hereinkommt -- ohne Kontakt ist sie offen.</summary>
public sealed record ProductionRoleInput(string Name, Guid? FunctionId = null, Guid? ContactId = null, string? Note = null);

public sealed record ProductionBudgetLineInput(string Title, decimal Amount, string? Category = null, string? Note = null);

public sealed record ProductionNeedInput(string Title, ProductionNeedKind Kind = ProductionNeedKind.Material, string? Quantity = null, bool IsDone = false, string? Note = null);

/// <summary>
/// Die Vorgaben einer Produktion, so wie sie als Ganzes gesetzt werden.
/// Jede Liste ersetzt die ihre; eine fehlende Liste laesst ihren Stand
/// unberuehrt -- so kann die Oberflaeche einen Abschnitt fuer sich speichern.
/// </summary>
public sealed record ProductionPlan(
    IReadOnlyCollection<ProductionRoleInput>? Roles = null,
    IReadOnlyCollection<ProductionBudgetLineInput>? BudgetLines = null,
    IReadOnlyCollection<ProductionNeedInput>? Needs = null);

/// <summary>
/// Eine Woche Leben an der Produktion: was angelegt, erledigt, geprobt,
/// bezahlt und abgelegt wurde. Der Herzschlag, den die Liste zeigt.
/// </summary>
public sealed record ActivityWeek(DateOnly Week, int Tasks, int Events, int Vouchers, int Documents)
{
    public int Total => Tasks + Events + Vouchers + Documents;
}

/// <summary>Eine Besetzung aus Sicht des Kontakts: welche Rolle in welcher Produktion.</summary>
public sealed record Casting(Production Production, ProductionRole Role);

/// <summary>
/// Was an einer Produktion haengt, in Zahlen -- fuer die Liste.
/// </summary>
/// <remarks>
/// Kosten und Einnahmen sind Bruttosummen der zugeordneten Belege: Eingang
/// ist Aufwand, Ausgang ist Ertrag. Das ist keine Buchhaltung, sondern die
/// Antwort auf „was hat Hamlet bisher gekostet" -- die Buchhaltung bleibt
/// im Journal.
/// </remarks>
public sealed record ProductionFigures(
    int Roles,
    int OpenRoles,
    int OpenTasks,
    int Tasks,
    int Events,
    DateTimeOffset? NextEvent,
    int Documents,
    decimal Planned,
    decimal Costs,
    decimal Income,
    int Tours,
    int Needs,
    int OpenNeeds,
    int Papers,
    int OpenPapers,
    IReadOnlyList<ActivityWeek> Activity);

/// <summary>Eine Produktion mit allem, was an ihr haengt -- fuer die Uebersicht.</summary>
public sealed record ProductionOverview(
    Production Production,
    ProductionFigures Figures,
    IReadOnlyList<TaskItem> Tasks,
    IReadOnlyList<CalendarEvent> Events,
    IReadOnlyList<EventSeries> Series,
    IReadOnlyList<Voucher> Vouchers,
    IReadOnlyList<Document> Documents,
    IReadOnlyList<Tour> Tours);

/// <summary>
/// Die Produktionen des Hauses -- siehe ADR-0011.
/// </summary>
/// <remarks>
/// Der Dienst fuehrt die Produktion und liest zusammen, was die anderen
/// Module an sie geheftet haben. Er schreibt dort nichts: Eine Aufgabe
/// bekommt ihre Produktion im Aufgabenmodul, ein Beleg in den Finanzen.
/// Nur der Beleg macht eine Ausnahme, weil er festgeschrieben sein kann und
/// die Zuordnung trotzdem nachgetragen werden soll (<see cref="AssignVoucherAsync"/>).
/// </remarks>
public class ProductionService(SouffleurDbContext context, ModuleRegistry modules, DocumentService documents)
{
    public const int MaxResults = 500;

    /// <summary>Wie weit der Herzschlag zurueckreicht -- ein halbes Jahr, wochenweise.</summary>
    public const int ActivityWeeks = 26;

    private IQueryable<Production> Query() => context.Productions
        .Include(p => p.Roles).ThenInclude(r => r.Contact)
        .Include(p => p.Roles).ThenInclude(r => r.Function)
        .Include(p => p.BudgetLines)
        .Include(p => p.Needs);

    /// <summary>
    /// Alle Produktionen: die laufenden zuerst, darin die naechste Premiere
    /// vorn; Abgespieltes hinten, das Juengste zuerst.
    /// </summary>
    public async Task<List<Production>> ListAsync(bool includeArchived = true, CancellationToken token = default)
    {
        IQueryable<Production> query = Query().AsNoTracking();
        if (!includeArchived)
        {
            query = query.Where(p => p.Status != ProductionStatus.Archived);
        }

        List<Production> found = await query.Take(MaxResults).ToListAsync(token);
        foreach (Production production in found)
        {
            Arrange(production);
        }

        return [.. found
            .OrderBy(p => p.Status == ProductionStatus.Archived)
            .ThenBy(p => p.Status == ProductionStatus.Archived ? DateOnly.MaxValue : p.PremiereDate ?? DateOnly.MaxValue)
            .ThenByDescending(p => p.Status == ProductionStatus.Archived ? p.ClosingDate ?? p.PremiereDate : null)
            .ThenBy(p => p.Title)];
    }

    /// <summary>Die Zahlen zu jeder Produktion -- eine Abfrage je Quelle, nicht je Produktion.</summary>
    public async Task<Dictionary<Guid, ProductionFigures>> FiguresAsync(
        IReadOnlyCollection<Guid> ids, DocumentViewer viewer, CancellationToken token = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        var roles = await context.ProductionRoles
            .Where(r => ids.Contains(r.ProductionId))
            .GroupBy(r => r.ProductionId)
            .Select(g => new { g.Key, Count = g.Count(), Open = g.Count(r => r.ContactId == null) })
            .ToDictionaryAsync(x => x.Key, token);

        var planned = await context.ProductionBudgetLines
            .Where(l => ids.Contains(l.ProductionId))
            .GroupBy(l => l.ProductionId)
            .Select(g => new { g.Key, Sum = g.Sum(l => l.Amount) })
            .ToDictionaryAsync(x => x.Key, x => x.Sum, token);

        var needs = await context.ProductionNeeds
            .Where(n => ids.Contains(n.ProductionId))
            .GroupBy(n => n.ProductionId)
            .Select(g => new
            {
                g.Key,
                Material = g.Count(n => n.Kind == ProductionNeedKind.Material),
                OpenMaterial = g.Count(n => n.Kind == ProductionNeedKind.Material && !n.IsDone),
                Papers = g.Count(n => n.Kind == ProductionNeedKind.Document),
                OpenPapers = g.Count(n => n.Kind == ProductionNeedKind.Document && !n.IsDone),
            })
            .ToDictionaryAsync(x => x.Key, token);

        var tasks = await context.TaskItems
            .Where(t => t.ProductionId != null && ids.Contains(t.ProductionId.Value))
            .GroupBy(t => t.ProductionId!.Value)
            .Select(g => new
            {
                g.Key,
                Count = g.Count(),
                Open = g.Count(t => t.State != TaskState.Done && t.State != TaskState.Dropped),
            })
            .ToDictionaryAsync(x => x.Key, token);

        var events = await context.CalendarEvents
            .Where(e => e.ProductionId != null && ids.Contains(e.ProductionId.Value) && e.Status != EventStatus.Cancelled)
            .GroupBy(e => e.ProductionId!.Value)
            .Select(g => new
            {
                g.Key,
                Count = g.Count(),
                Next = g.Where(e => e.StartsAt >= now).Min(e => (DateTimeOffset?)e.StartsAt),
            })
            .ToDictionaryAsync(x => x.Key, token);

        var vouchers = await context.Vouchers
            .Where(v => v.ProductionId != null && ids.Contains(v.ProductionId.Value))
            .GroupBy(v => v.ProductionId!.Value)
            .Select(g => new
            {
                g.Key,
                Costs = g.Where(v => v.Direction == VoucherDirection.Incoming).Sum(v => v.GrossAmount),
                Income = g.Where(v => v.Direction == VoucherDirection.Outgoing).Sum(v => v.GrossAmount),
            })
            .ToDictionaryAsync(x => x.Key, token);

        // Nur die Dokumente zaehlen, die diese Person sehen darf -- sonst
        // verriete die Zahl, dass es verborgene gibt.
        IQueryable<Guid> visible = documents.Visible(viewer).Select(d => d.Id);
        var docs = await context.DocumentLinks
            .Where(l => l.Target == DocumentTarget.Production && ids.Contains(l.TargetId) && visible.Contains(l.DocumentId))
            .GroupBy(l => l.TargetId)
            .Select(g => new { g.Key, Count = g.Select(l => l.DocumentId).Distinct().Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, token);

        Dictionary<Guid, int> tours = modules.IsEnabled(SouffleurModule.Touring)
            ? await context.Tours
                .Where(t => t.ProductionId != null && ids.Contains(t.ProductionId.Value))
                .GroupBy(t => t.ProductionId!.Value)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, token)
            : [];

        Dictionary<Guid, IReadOnlyList<ActivityWeek>> activity = await ActivityAsync(ids, token);

        return ids.ToDictionary(id => id, id => new ProductionFigures(
            roles.TryGetValue(id, out var r) ? r.Count : 0,
            r?.Open ?? 0,
            tasks.TryGetValue(id, out var t) ? t.Open : 0,
            t?.Count ?? 0,
            events.TryGetValue(id, out var e) ? e.Count : 0,
            e?.Next,
            docs.GetValueOrDefault(id),
            planned.GetValueOrDefault(id),
            vouchers.TryGetValue(id, out var v) ? v.Costs : 0m,
            v?.Income ?? 0m,
            tours.GetValueOrDefault(id),
            needs.TryGetValue(id, out var n) ? n.Material : 0,
            n?.OpenMaterial ?? 0,
            n?.Papers ?? 0,
            n?.OpenPapers ?? 0,
            activity[id]));
    }

    /// <summary>
    /// Der Herzschlag: je Woche, was an der Produktion passiert ist. Eine
    /// Aufgabe zaehlt in der Woche, in der sie angelegt, geaendert oder
    /// abgeschlossen wurde; ein Termin, wenn er stattfand; ein Beleg mit
    /// seinem Datum; ein Dokument, als es angeheftet wurde. Nur Vergangenes
    /// bis zur laufenden Woche -- der Plan steht anderswo.
    /// </summary>
    public async Task<Dictionary<Guid, IReadOnlyList<ActivityWeek>>> ActivityAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken token = default)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        DateOnly thisWeek = Monday(today);
        DateOnly first = thisWeek.AddDays(-7 * (ActivityWeeks - 1));
        DateTimeOffset from = new(first.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        DateTimeOffset until = new(thisWeek.AddDays(7).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var tasks = await context.TaskItems
            .Where(t => t.ProductionId != null && ids.Contains(t.ProductionId.Value))
            .Select(t => new { Id = t.ProductionId!.Value, t.CreatedAt, t.ModifiedAt, t.ClosedAt })
            .ToListAsync(token);

        var events = await context.CalendarEvents
            .Where(e => e.ProductionId != null && ids.Contains(e.ProductionId.Value)
                && e.Status != EventStatus.Cancelled && e.StartsAt >= from && e.StartsAt < until)
            .Select(e => new { Id = e.ProductionId!.Value, e.StartsAt })
            .ToListAsync(token);

        var vouchers = await context.Vouchers
            .Where(v => v.ProductionId != null && ids.Contains(v.ProductionId.Value) && v.Date >= first)
            .Select(v => new { Id = v.ProductionId!.Value, v.Date })
            .ToListAsync(token);

        var documents = await context.DocumentLinks
            .Where(l => l.Target == DocumentTarget.Production && ids.Contains(l.TargetId) && l.CreatedAt >= from)
            .Select(l => new { Id = l.TargetId, l.CreatedAt })
            .ToListAsync(token);

        var counts = new Dictionary<(Guid, DateOnly), int[]>();
        void Count(Guid id, DateOnly day, int slot)
        {
            DateOnly week = Monday(day);
            if (week < first || week > thisWeek)
            {
                return;
            }

            if (!counts.TryGetValue((id, week), out int[]? row))
            {
                counts[(id, week)] = row = new int[4];
            }

            row[slot]++;
        }

        foreach (var t in tasks)
        {
            // Jede Beruehrung zaehlt einmal je Woche -- angelegt und am selben
            // Tag erledigt ist ein Schlag, nicht zwei.
            HashSet<DateOnly> weeks = [Monday(DateOnly.FromDateTime(t.CreatedAt.UtcDateTime))];
            if (t.ModifiedAt is { } modified) weeks.Add(Monday(DateOnly.FromDateTime(modified.UtcDateTime)));
            if (t.ClosedAt is { } closed) weeks.Add(Monday(DateOnly.FromDateTime(closed.UtcDateTime)));
            foreach (DateOnly week in weeks) Count(t.Id, week, 0);
        }

        foreach (var e in events) Count(e.Id, DateOnly.FromDateTime(e.StartsAt.UtcDateTime), 1);
        foreach (var v in vouchers) Count(v.Id, v.Date, 2);
        foreach (var d in documents) Count(d.Id, DateOnly.FromDateTime(d.CreatedAt.UtcDateTime), 3);

        return ids.ToDictionary(id => id, id => (IReadOnlyList<ActivityWeek>)
        [
            .. Enumerable.Range(0, ActivityWeeks).Select(i =>
            {
                DateOnly week = first.AddDays(7 * i);
                int[] row = counts.GetValueOrDefault((id, week)) ?? new int[4];
                return new ActivityWeek(week, row[0], row[1], row[2], row[3]);
            }),
        ]);
    }

    private static DateOnly Monday(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    public async Task<Production?> FindAsync(Guid id, CancellationToken token = default)
    {
        Production? production = await Query().AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, token);
        if (production is not null)
        {
            Arrange(production);
        }

        return production;
    }

    /// <summary>Die Produktion mit allem, was an ihr haengt -- gelesen aus den Modulen, die es fuehren.</summary>
    public async Task<ProductionOverview?> OverviewAsync(Guid id, DocumentViewer viewer, CancellationToken token = default)
    {
        Production? production = await FindAsync(id, token);
        if (production is null)
        {
            return null;
        }

        List<TaskItem> tasks = await context.TaskItems
            .Where(t => t.ProductionId == id)
            .Include(t => t.Assignee)
            .Include(t => t.Group)
            .Include(t => t.Category)
            .AsNoTracking()
            .OrderBy(t => t.State == TaskState.Done || t.State == TaskState.Dropped)
            .ThenBy(t => t.DueDate == null)
            .ThenBy(t => t.DueDate)
            .ThenBy(t => t.SortOrder)
            .Take(MaxResults)
            .ToListAsync(token);

        List<CalendarEvent> events = await context.CalendarEvents
            .Where(e => e.ProductionId == id)
            .AsNoTracking()
            .OrderBy(e => e.StartsAt)
            .Take(MaxResults)
            .ToListAsync(token);

        List<EventSeries> series = await context.EventSeries
            .Where(s => s.ProductionId == id)
            .AsNoTracking()
            .OrderBy(s => s.StartDate)
            .ToListAsync(token);

        List<Voucher> vouchers = await context.Vouchers
            .Where(v => v.ProductionId == id)
            .Include(v => v.Contact)
            .AsNoTracking()
            .OrderByDescending(v => v.Date)
            .ThenByDescending(v => v.Number)
            .Take(MaxResults)
            .ToListAsync(token);

        List<Guid> documentIds = await context.DocumentLinks
            .Where(l => l.Target == DocumentTarget.Production && l.TargetId == id)
            .Select(l => l.DocumentId)
            .Distinct()
            .ToListAsync(token);

        List<Document> docs = await documents.Visible(viewer)
            .Where(d => documentIds.Contains(d.Id))
            .Include(d => d.Tags)
            .AsNoTracking()
            .OrderBy(d => d.Name)
            .ToListAsync(token);

        List<Tour> tours = modules.IsEnabled(SouffleurModule.Touring)
            ? await context.Tours
                .Where(t => t.ProductionId == id)
                .Include(t => t.Stops).ThenInclude(s => s.Venue)
                .Include(t => t.Stops).ThenInclude(s => s.Performances)
                .AsNoTracking()
                .OrderBy(t => t.Title)
                .ToListAsync(token)
            : [];

        ProductionFigures figures = (await FiguresAsync([id], viewer, token))[id];

        return new ProductionOverview(production, figures, tasks, events, series, vouchers, docs, tours);
    }

    public async Task<Production> AddAsync(
        Production production,
        ProductionPlan? plan = null,
        CancellationToken token = default)
    {
        if (plan is not null)
        {
            await ApplyPlanAsync(production, plan, isNew: true, token);
        }

        EnsureValid(production);

        context.Productions.Add(production);
        await context.SaveChangesAsync(token);
        return production;
    }

    public async Task<Production> UpdateAsync(
        Guid id,
        Action<Production> change,
        ProductionPlan? plan = null,
        CancellationToken token = default)
    {
        Production production = await context.Productions
            .Include(p => p.Roles)
            .Include(p => p.BudgetLines)
            .Include(p => p.Needs)
            .FirstOrDefaultAsync(p => p.Id == id, token)
            ?? throw new ProductionException("Diese Produktion gibt es nicht.");

        change(production);

        if (plan is not null)
        {
            await ApplyPlanAsync(production, plan, isNew: false, token);
        }

        EnsureValid(production);
        await context.SaveChangesAsync(token);
        return production;
    }

    /// <summary>
    /// Eine einzelne Rolle dazunehmen -- der Weg vom Kontakt aus: „Diese
    /// Person spielt in Hamlet mit."
    /// </summary>
    public async Task<ProductionRole> AddRoleAsync(Guid productionId, ProductionRoleInput input, CancellationToken token = default)
    {
        Production production = await context.Productions
            .Include(p => p.Roles)
            .FirstOrDefaultAsync(p => p.Id == productionId, token)
            ?? throw new ProductionException("Diese Produktion gibt es nicht.");

        List<ProductionRole> roles = await RolesFromAsync([input], token);
        ProductionRole role = roles[0];
        role.ProductionId = production.Id;
        role.SortOrder = (production.Roles.Count == 0 ? 0 : production.Roles.Max(r => r.SortOrder)) + 10;
        production.Roles.Add(role);
        context.ProductionRoles.Add(role);

        EnsureValid(production);
        await context.SaveChangesAsync(token);
        return role;
    }

    public async Task RemoveRoleAsync(Guid productionId, Guid roleId, CancellationToken token = default)
    {
        ProductionRole role = await context.ProductionRoles
            .FirstOrDefaultAsync(r => r.Id == roleId && r.ProductionId == productionId, token)
            ?? throw new ProductionException("Diese Rolle gibt es nicht.");

        context.ProductionRoles.Remove(role);
        await context.SaveChangesAsync(token);
    }

    /// <summary>In welchen Produktionen jemand besetzt ist -- fuer den Kontaktbogen.</summary>
    public async Task<List<Casting>> CastingsAsync(Guid contactId, CancellationToken token = default)
    {
        List<ProductionRole> roles = await context.ProductionRoles
            .Where(r => r.ContactId == contactId)
            .Include(r => r.Production)
            .Include(r => r.Function)
            .AsNoTracking()
            .ToListAsync(token);

        return [.. roles
            .OrderBy(r => r.Production.Status == ProductionStatus.Archived)
            .ThenBy(r => r.Production.PremiereDate ?? DateOnly.MaxValue)
            .ThenBy(r => r.Production.Title)
            .ThenBy(r => r.SortOrder)
            .Select(r => new Casting(r.Production, r))];
    }

    /// <summary>
    /// Loeschen geht nur, solange nichts an der Produktion haengt. Sonst
    /// wird sie abgespielt -- Belege und Dokumente brauchen ihre Klammer.
    /// </summary>
    public async Task DeleteAsync(Guid id, CancellationToken token = default)
    {
        Production production = await context.Productions.FirstOrDefaultAsync(p => p.Id == id, token)
            ?? throw new ProductionException("Diese Produktion gibt es nicht.");

        List<string> attached = [];
        Count(attached, await context.Vouchers.CountAsync(v => v.ProductionId == id, token), "Beleg", "Belege");
        Count(attached, await context.TaskItems.CountAsync(t => t.ProductionId == id, token), "Aufgabe", "Aufgaben");
        Count(attached,
            await context.CalendarEvents.CountAsync(e => e.ProductionId == id, token)
            + await context.EventSeries.CountAsync(s => s.ProductionId == id, token),
            "Termin", "Termine");
        Count(attached,
            await context.DocumentLinks.CountAsync(l => l.Target == DocumentTarget.Production && l.TargetId == id, token),
            "Dokument", "Dokumente");
        Count(attached, await context.Tours.CountAsync(t => t.ProductionId == id, token), "Tournee", "Tourneen");

        if (attached.Count > 0)
        {
            throw new ProductionException(
                $"An „{production.Title}“ hängen noch {string.Join(", ", attached)}. "
                + "Eine Produktion mit Anhang wird abgespielt, nicht gelöscht.");
        }

        // Lesezeichen sind kein Anhang, der das Loeschen verhindert -- sie
        // zeigen nur irgendwohin. Sie gehen mit.
        context.Bookmarks.RemoveRange(
            context.Bookmarks.Where(b => b.Target == BookmarkTarget.Production && b.TargetId == id));
        context.Productions.Remove(production);
        await context.SaveChangesAsync(token);
    }

    /// <summary>
    /// Einen Beleg der Produktion zuordnen oder die Zuordnung loesen -- auch
    /// nach dem Festschreiben. Die Buchhaltung bleibt unberuehrt; es aendert
    /// sich nur, wo das Haus den Beleg einsortiert.
    /// </summary>
    public async Task AssignVoucherAsync(Guid? productionId, Guid voucherId, CancellationToken token = default)
    {
        if (productionId is { } id && !await context.Productions.AnyAsync(p => p.Id == id, token))
        {
            throw new ProductionException("Diese Produktion gibt es nicht.");
        }

        Voucher voucher = await context.Vouchers.FirstOrDefaultAsync(v => v.Id == voucherId, token)
            ?? throw new ProductionException("Den Beleg gibt es nicht.");

        voucher.ProductionId = productionId;
        await context.SaveChangesAsync(token);
    }

    private static void Count(List<string> into, int count, string one, string many)
    {
        if (count > 0)
        {
            into.Add($"{count} {(count == 1 ? one : many)}");
        }
    }

    private static void EnsureValid(Production production)
    {
        IReadOnlyList<string> problems = production.Validate();
        if (problems.Count > 0)
        {
            throw new ProductionException(problems);
        }
    }

    /// <summary>
    /// Die Listen des Plans als Ganzes ersetzen. Neue Zeilen an einem
    /// geladenen Objekt muessen ausdruecklich hinzugefuegt werden -- sonst
    /// macht EF ein UPDATE auf eine Zeile, die es nicht gibt.
    /// </summary>
    private async Task ApplyPlanAsync(Production production, ProductionPlan plan, bool isNew, CancellationToken token)
    {
        if (plan.Roles is not null)
        {
            List<ProductionRole> roles = await RolesFromAsync(plan.Roles, token);
            Replace(production.Roles, roles, context.ProductionRoles, production.Id, isNew);
        }

        if (plan.BudgetLines is not null)
        {
            List<ProductionBudgetLine> lines = [.. plan.BudgetLines.Select((l, i) => new ProductionBudgetLine
            {
                Title = l.Title.Trim(),
                Category = Clean(l.Category),
                Amount = l.Amount,
                Note = Clean(l.Note),
                SortOrder = (i + 1) * 10,
            })];
            Replace(production.BudgetLines, lines, context.ProductionBudgetLines, production.Id, isNew);
        }

        if (plan.Needs is not null)
        {
            List<ProductionNeed> needs = [.. plan.Needs.Select((n, i) => new ProductionNeed
            {
                Kind = n.Kind,
                Title = n.Title.Trim(),
                Quantity = Clean(n.Quantity),
                IsDone = n.IsDone,
                Note = Clean(n.Note),
                SortOrder = (i + 1) * 10,
            })];
            Replace(production.Needs, needs, context.ProductionNeeds, production.Id, isNew);
        }
    }

    private static void Replace<T>(List<T> current, List<T> replacement, DbSet<T> set, Guid productionId, bool isNew)
        where T : Entity
    {
        if (!isNew)
        {
            set.RemoveRange(current);
        }

        current.Clear();
        current.AddRange(replacement);

        if (!isNew)
        {
            foreach (T row in replacement)
            {
                SetProduction(row, productionId);
                set.Add(row);
            }
        }
    }

    private static void SetProduction(Entity row, Guid productionId)
    {
        switch (row)
        {
            case ProductionRole r: r.ProductionId = productionId; break;
            case ProductionBudgetLine l: l.ProductionId = productionId; break;
            case ProductionNeed n: n.ProductionId = productionId; break;
        }
    }

    private async Task<List<ProductionRole>> RolesFromAsync(
        IReadOnlyCollection<ProductionRoleInput> inputs, CancellationToken token)
    {
        List<Guid> ids = [.. inputs.Where(i => i.ContactId != null).Select(i => i.ContactId!.Value).Distinct()];
        int known = await context.Contacts.CountAsync(c => ids.Contains(c.Id), token);
        if (known != ids.Count)
        {
            throw new ProductionException("Eine der Personen steht nicht im Adressbuch.");
        }

        List<Guid> functions = [.. inputs.Where(i => i.FunctionId != null).Select(i => i.FunctionId!.Value).Distinct()];
        int knownFunctions = await context.CatalogEntries.CountAsync(
            e => functions.Contains(e.Id) && e.Catalog == CatalogKind.ParticipationRole, token);
        if (knownFunctions != functions.Count)
        {
            throw new ProductionException("Eine der Funktionen steht nicht in der Liste der Funktionen am Termin.");
        }

        return [.. inputs.Select((i, index) => new ProductionRole
        {
            Name = i.Name?.Trim() ?? string.Empty,
            FunctionId = i.FunctionId,
            ContactId = i.ContactId,
            Note = Clean(i.Note),
            SortOrder = (index + 1) * 10,
        })];
    }

    private static string? Clean(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>Die Listen in der Reihenfolge, in der sie eingegeben wurden -- Rollen im Stab zuerst nach Bereich.</summary>
    private static void Arrange(Production production)
    {
        production.Roles = [.. production.Roles.OrderBy(r => r.SortOrder)];
        production.BudgetLines = [.. production.BudgetLines.OrderBy(l => l.SortOrder)];
        production.Needs = [.. production.Needs.OrderBy(n => n.SortOrder)];
    }
}
