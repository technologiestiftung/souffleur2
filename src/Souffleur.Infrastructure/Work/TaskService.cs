using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Accounting;
using Souffleur.Domain.Catalogs;
using Souffleur.Domain.Contacts;
using Souffleur.Domain.Work;
using Souffleur.Infrastructure.Persistence;
using Souffleur.Domain.Identity;

namespace Souffleur.Infrastructure.Work;

public class TaskException(IReadOnlyList<string> problems)
    : Exception(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;

    public TaskException(string problem) : this([problem])
    {
    }
}

/// <summary>
/// Die Aufgaben des Hauses -- siehe ADR-0007.
/// </summary>
public class TaskService(SouffleurDbContext context, TimeProvider time)
{
    /// <summary>
    /// Wie viele Aufgaben eine Abfrage höchstens liefert. Wer mehr hat,
    /// braucht einen Filter, keine längere Liste.
    /// </summary>
    public const int MaxResults = 500;

    /// <summary>
    /// Wie tief Aufgaben ineinander stecken dürfen. Eine Checkliste in einer
    /// Checkliste ist noch verständlich, die dritte Ebene nicht mehr.
    /// </summary>
    public const int MaxDepth = 2;

    public async Task<List<TaskItem>> ListAsync(
        IReadOnlyCollection<TaskState>? states = null,
        Guid? categoryId = null,
        Guid? assigneeId = null,
        bool includeClosed = false,
        IReadOnlyCollection<Guid>? groupIds = null,
        Guid? productionId = null,
        CancellationToken token = default)
    {
        IQueryable<TaskItem> query = context.TaskItems
            .Include(t => t.Category)
            .Include(t => t.Assignee)
            .Include(t => t.Group)
            .Include(t => t.Production)
            .Include(t => t.CostCenter)
            .Include(t => t.Contacts).ThenInclude(c => c.Contact)
            .AsNoTracking();

        if (productionId is { } production)
        {
            query = query.Where(t => t.ProductionId == production);
        }

        if (states is { Count: > 0 })
        {
            query = query.Where(t => states.Contains(t.State));
        }
        else if (!includeClosed)
        {
            query = query.Where(t => t.State != TaskState.Done && t.State != TaskState.Dropped);
        }

        if (categoryId is { } category)
        {
            query = query.Where(t => t.CategoryId == category);
        }

        if (assigneeId is { } assignee)
        {
            query = query.Where(t => t.AssigneeId == assignee);
        }

        // Eine leere Menge heisst: in keiner Gruppe -- dann gibt es auch
        // nichts fuer meine Gruppen. Null heisst: nicht danach gefragt.
        if (groupIds is not null)
        {
            query = query.Where(t => t.GroupId != null && groupIds.Contains(t.GroupId.Value));
        }

        // Was eine Frist hat, zuerst und nach ihr sortiert: Danach richtet
        // sich, was als Nächstes dran ist. Fristloses hängt hinten an.
        List<TaskItem> found = await query
            .OrderBy(t => t.DueDate == null)
            .ThenBy(t => t.DueDate)
            .ThenBy(t => t.SortOrder)
            .ThenBy(t => t.Title)
            .Take(MaxResults)
            .ToListAsync(token);

        return Arrange(found);
    }

    /// <summary>
    /// Teilaufgaben hinter ihre Aufgabe stellen, und die Aufgaben nach der
    /// frühesten Frist ordnen, die in ihnen steckt.
    /// </summary>
    /// <remarks>
    /// Ohne das reißt die Sortierung nach Frist die Checklisten auseinander:
    /// „Pressetext verschicken" stünde zwischen fremden Aufgaben, weil seine
    /// eigene Frist früher liegt als die der Premiere, zu der es gehört.
    ///
    /// Und eine Aufgabe ohne eigene Frist, unter der etwas Dringendes hängt,
    /// stünde ganz hinten bei den fristlosen -- obwohl sie das Nächste ist,
    /// was ansteht.
    /// </remarks>
    private static List<TaskItem> Arrange(List<TaskItem> tasks)
    {
        HashSet<Guid> present = [.. tasks.Select(t => t.Id)];

        // Wessen Aufgabe gerade weggefiltert ist, steht für sich -- sonst
        // verschwände die Teilaufgabe ganz.
        bool IsRoot(TaskItem task) =>
            task.ParentId is not { } parent || !present.Contains(parent);

        ILookup<Guid, TaskItem> children = tasks
            .Where(task => !IsRoot(task))
            .ToLookup(task => task.ParentId!.Value);

        Dictionary<Guid, DateOnly?> earliest = tasks.Where(IsRoot).ToDictionary(
            task => task.Id,
            task => children[task.Id]
                .Select(child => child.DueDate)
                .Append(task.DueDate)
                .Where(due => due is not null)
                .Min());

        List<TaskItem> result = [];

        foreach (TaskItem root in tasks.Where(IsRoot)
                     .OrderBy(task => earliest[task.Id] is null)
                     .ThenBy(task => earliest[task.Id])
                     .ThenBy(task => task.SortOrder)
                     .ThenBy(task => task.Title))
        {
            result.Add(root);
            result.AddRange(children[root.Id]
                .OrderBy(child => child.DueDate is null)
                .ThenBy(child => child.DueDate)
                .ThenBy(child => child.SortOrder)
                .ThenBy(child => child.Title));
        }

        return result;
    }

    /// <summary>Aufgaben mit einer Frist im Zeitraum -- für den Kalender.</summary>
    public async Task<List<TaskItem>> DueBetweenAsync(
        DateOnly from, DateOnly to, CancellationToken token = default) =>
        await context.TaskItems
            .Include(t => t.Category)
            .Include(t => t.Assignee)
            .Include(t => t.Group)
            .Include(t => t.Contacts).ThenInclude(c => c.Contact)
            .Where(t => t.DueDate != null
                        && t.DueDate >= from
                        && t.DueDate <= to
                        && t.State != TaskState.Done
                        && t.State != TaskState.Dropped)
            .AsNoTracking()
            .ToListAsync(token);

    public async Task<TaskItem?> FindAsync(Guid id, CancellationToken token = default) =>
        await context.TaskItems
            .Include(t => t.Category)
            .Include(t => t.Assignee)
            .Include(t => t.Group)
            .Include(t => t.Production)
            .Include(t => t.CostCenter)
            .Include(t => t.Contacts).ThenInclude(c => c.Contact)
            .Include(t => t.Children)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, token);

    public async Task<TaskItem> AddAsync(TaskItem task, CancellationToken token = default)
    {
        await ValidateAsync(task, token);
        ApplyClosing(task);

        if (task.SortOrder == 0)
        {
            int highest = await context.TaskItems
                .Where(t => t.ParentId == task.ParentId)
                .MaxAsync(t => (int?)t.SortOrder, token) ?? 0;
            task.SortOrder = highest + 10;
        }

        context.TaskItems.Add(task);
        await context.SaveChangesAsync(token);
        return task;
    }

    public async Task<TaskItem> UpdateAsync(
        Guid id, Action<TaskItem> apply, CancellationToken token = default)
    {
        TaskItem found = await TrackedAsync(id, token);
        apply(found);
        await ValidateAsync(found, token);
        ApplyClosing(found);
        await context.SaveChangesAsync(token);
        return found;
    }

    /// <summary>
    /// Den Stand ändern. Eigener Weg, weil das die häufigste Änderung ist --
    /// und die einzige, die auch aus einer Liste heraus geschieht.
    /// </summary>
    public Task<TaskItem> SetStateAsync(
        Guid id, TaskState state, CancellationToken token = default) =>
        UpdateAsync(id, task => task.State = state, token);

    /// <summary>
    /// Löschen geht nur, solange nichts darunter hängt. Sonst verlören die
    /// Teilaufgaben ihren Zusammenhang.
    /// </summary>
    public async Task DeleteAsync(Guid id, CancellationToken token = default)
    {
        TaskItem found = await TrackedAsync(id, token);

        int children = await context.TaskItems.CountAsync(t => t.ParentId == id, token);
        if (children > 0)
        {
            throw new TaskException(
                $"„{found.Title}“ hat {children} Teilaufgaben. Erst die wegnehmen " +
                "oder die ganze Aufgabe verwerfen -- verworfen bleibt sie lesbar.");
        }

        int waiting = await context.TaskDependencies.CountAsync(d => d.DependsOnId == id, token);
        if (waiting > 0)
        {
            throw new TaskException(
                $"Auf „{found.Title}“ warten {waiting} Aufgaben. Erst die Verbindung lösen.");
        }

        context.TaskItems.Remove(found);
        await context.SaveChangesAsync(token);
    }

    public async Task ReorderAsync(IReadOnlyList<Guid> order, CancellationToken token = default)
    {
        List<TaskItem> all = await context.TaskItems
            .Where(t => order.Contains(t.Id))
            .ToListAsync(token);

        for (int index = 0; index < order.Count; index++)
        {
            if (all.FirstOrDefault(t => t.Id == order[index]) is { } task)
            {
                task.SortOrder = (index + 1) * 10;
            }
        }

        await context.SaveChangesAsync(token);
    }

    // --- Zeitstrahl ---

    /// <summary>
    /// Was im Zeitraum einen Balken hat, samt Abhängigkeiten.
    /// </summary>
    /// <remarks>
    /// Aufgaben ohne Anfang und ohne Frist erscheinen nicht -- es gibt nichts
    /// aufzutragen. Die Ansicht sagt, wie viele das sind, damit sie nicht
    /// stillschweigend verschwinden.
    /// </remarks>
    public async Task<Timeline> TimelineAsync(
        DateOnly from,
        DateOnly to,
        bool includeClosed = false,
        CancellationToken token = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        List<TaskItem> all = await context.TaskItems
            .Include(t => t.Category)
            .Include(t => t.Assignee)
            .Include(t => t.Group)
            .Where(t => includeClosed
                        || (t.State != TaskState.Done && t.State != TaskState.Dropped))
            .AsNoTracking()
            .ToListAsync(token);

        // Der Balken muss den Zeitraum berühren, nicht in ihm liegen: Ein
        // Vorgang über drei Monate soll auch dann erscheinen, wenn man in
        // seine Mitte blickt.
        List<TaskItem> shown =
        [
            .. all.Where(t => t.Span is { } span && span.From <= to && span.To >= from)
                  .OrderBy(t => t.Span!.Value.From)
                  .ThenBy(t => t.Span!.Value.To)
                  .ThenBy(t => t.Title),
        ];

        HashSet<Guid> visible = [.. shown.Select(t => t.Id)];

        List<TaskDependency> links = await context.TaskDependencies
            .AsNoTracking()
            .ToListAsync(token);

        // Nur Verbindungen, deren beide Enden man sieht -- ein Pfeil ins
        // Nichts erklärt nichts.
        List<TaskDependency> edges =
        [
            .. links.Where(l => visible.Contains(l.TaskId) && visible.Contains(l.DependsOnId)),
        ];

        Dictionary<Guid, TaskItem> byId = shown.ToDictionary(t => t.Id);

        List<DependencyBreach> breaches =
        [
            .. links
                .Where(l => byId.ContainsKey(l.TaskId) && byId.ContainsKey(l.DependsOnId))
                .Select(l => (Link: l, Task: byId[l.TaskId], Earlier: byId[l.DependsOnId]))
                .Where(x => Violates(x.Task, x.Earlier))
                .Select(x => new DependencyBreach(
                    x.Task.Id, x.Task.Title, x.Earlier.Id, x.Earlier.Title)),
        ];

        int unscheduled = all.Count(t => !t.IsScheduled);

        return new Timeline(from, to, shown, edges, breaches, unscheduled);
    }

    /// <summary>
    /// Ob die Reihenfolge nicht mehr aufgeht: Die Aufgabe beginnt, bevor das
    /// fertig ist, worauf sie wartet.
    /// </summary>
    /// <remarks>
    /// Gleicher Tag ist in Ordnung. Im Theater wird vormittags abgebaut und
    /// nachmittags eingerichtet; auf den Tag genau zu rechnen, hieße zu
    /// behaupten, man wüsste es besser.
    /// </remarks>
    private static bool Violates(TaskItem task, TaskItem earlier) =>
        task.Span is { } span && earlier.Span is { } before && span.From < before.To;

    public async Task<TaskDependency> LinkAsync(
        Guid taskId, Guid dependsOnId, CancellationToken token = default)
    {
        if (taskId == dependsOnId)
        {
            throw new TaskException("Eine Aufgabe kann nicht auf sich selbst warten.");
        }

        if (await context.TaskDependencies.AnyAsync(
                d => d.TaskId == taskId && d.DependsOnId == dependsOnId, token))
        {
            return await context.TaskDependencies.FirstAsync(
                d => d.TaskId == taskId && d.DependsOnId == dependsOnId, token);
        }

        foreach (Guid id in new[] { taskId, dependsOnId })
        {
            if (!await context.TaskItems.AnyAsync(t => t.Id == id, token))
            {
                throw new TaskException("Die Aufgabe gibt es nicht.");
            }
        }

        if (await WouldCircleAsync(taskId, dependsOnId, token))
        {
            throw new TaskException(
                "Das ergäbe einen Kreis: Jede der beiden Aufgaben wartete auf die andere.");
        }

        var link = new TaskDependency { TaskId = taskId, DependsOnId = dependsOnId };
        context.TaskDependencies.Add(link);
        await context.SaveChangesAsync(token);
        return link;
    }

    public async Task UnlinkAsync(
        Guid taskId, Guid dependsOnId, CancellationToken token = default)
    {
        TaskDependency? link = await context.TaskDependencies.FirstOrDefaultAsync(
            d => d.TaskId == taskId && d.DependsOnId == dependsOnId, token);

        if (link is not null)
        {
            context.TaskDependencies.Remove(link);
            await context.SaveChangesAsync(token);
        }
    }

    /// <summary>
    /// Ob die neue Verbindung einen Kreis schlösse -- dann wartete am Ende
    /// jede Aufgabe auf sich selbst, und keine könnte je beginnen.
    /// </summary>
    private async Task<bool> WouldCircleAsync(
        Guid taskId, Guid dependsOnId, CancellationToken token)
    {
        List<TaskDependency> all = await context.TaskDependencies
            .AsNoTracking()
            .ToListAsync(token);

        ILookup<Guid, Guid> waitsFor = all.ToLookup(d => d.TaskId, d => d.DependsOnId);

        // Von der Aufgabe, auf die gewartet werden soll, rückwärts: Kommt man
        // bei der wartenden an, ist der Kreis geschlossen.
        Queue<Guid> queue = new([dependsOnId]);
        HashSet<Guid> seen = [dependsOnId];

        while (queue.Count > 0)
        {
            Guid current = queue.Dequeue();

            if (current == taskId)
            {
                return true;
            }

            foreach (Guid next in waitsFor[current].Where(seen.Add))
            {
                queue.Enqueue(next);
            }
        }

        return false;
    }

    private async Task<TaskItem> TrackedAsync(Guid id, CancellationToken token) =>
        await context.TaskItems
            .Include(t => t.Contacts)
            .FirstOrDefaultAsync(t => t.Id == id, token)
        ?? throw new TaskException("Die Aufgabe gibt es nicht.");

    /// <summary>
    /// Beteiligte und verknuepfte Kontakte auf den gewuenschten Stand bringen:
    /// Wer fehlt, kommt dazu; wer nicht mehr genannt ist, geht. Dieselbe
    /// Person kann beteiligt und verknuepft sein, aber je Art nur einmal.
    /// </summary>
    /// <remarks>
    /// Ueber den DbSet und nicht nur ueber die Navigation, aus demselben
    /// Grund wie im Chat: Die Id ist schon vergeben, und ein an eine geladene
    /// Sammlung gehaengtes Objekt hielte EF Core fuer vorhanden.
    /// </remarks>
    public void SetContacts(TaskItem task, IEnumerable<Guid> participants, IEnumerable<Guid> related)
    {
        var wanted = new HashSet<(Guid, TaskContactKind)>(
            participants.Select(id => (id, TaskContactKind.Participant))
                .Concat(related.Select(id => (id, TaskContactKind.Related))));

        foreach (TaskContact gone in task.Contacts.Where(c => !wanted.Contains((c.ContactId, c.Kind))).ToList())
        {
            task.Contacts.Remove(gone);
            if (context.Entry(gone).State != EntityState.Detached)
            {
                context.TaskContacts.Remove(gone);
            }
        }

        foreach ((Guid contactId, TaskContactKind kind) in wanted)
        {
            if (task.Contacts.Any(c => c.ContactId == contactId && c.Kind == kind))
            {
                continue;
            }

            var link = new TaskContact { TaskId = task.Id, ContactId = contactId, Kind = kind };
            task.Contacts.Add(link);
            if (context.Entry(task).State != EntityState.Detached)
            {
                context.TaskContacts.Add(link);
            }
        }
    }

    /// <summary>
    /// Den Abschlusszeitpunkt mitführen. Nicht vom Aufrufer gesetzt: Sonst
    /// steht irgendwann „erledigt" ohne Datum da, oder ein Datum an etwas,
    /// das wieder geöffnet wurde.
    /// </summary>
    private void ApplyClosing(TaskItem task)
    {
        if (task.State.IsClosed())
        {
            task.ClosedAt ??= time.GetUtcNow();
        }
        else
        {
            task.ClosedAt = null;
        }
    }

    private async Task ValidateAsync(TaskItem task, CancellationToken token)
    {
        List<string> problems = [.. task.Validate()];

        if (task.CategoryId is { } categoryId)
        {
            CatalogEntry? category = await context.CatalogEntries.FirstOrDefaultAsync(
                e => e.Id == categoryId && e.Catalog == CatalogKind.TaskCategory, token);

            if (category is null)
            {
                problems.Add("Die Kategorie gibt es nicht.");
            }
            else if (!category.IsActive)
            {
                problems.Add($"Die Kategorie „{category.Label}“ ist abgeschaltet.");
            }
        }

        if (task.AssigneeId is { } assigneeId)
        {
            Contact? assignee = await context.Contacts.FirstOrDefaultAsync(
                c => c.Id == assigneeId, token);

            if (assignee is null)
            {
                problems.Add("Den Kontakt gibt es nicht.");
            }
            else if (!assignee.IsActive)
            {
                problems.Add($"„{assignee.Name}“ ist im Adressbuch abgeschaltet.");
            }
        }

        if (task.GroupId is { } groupId)
        {
            Group? group = await context.Groups.FirstOrDefaultAsync(g => g.Id == groupId, token);

            if (group is null)
            {
                problems.Add("Die Gruppe gibt es nicht.");
            }
            else if (!group.IsActive)
            {
                problems.Add($"Die Gruppe „{group.Name}“ ist stillgelegt.");
            }
        }

        if (task.ProductionId is { } productionId
            && !await context.Productions.AnyAsync(p => p.Id == productionId, token))
        {
            problems.Add("Diese Produktion gibt es nicht.");
        }

        if (task.CostCenterId is { } costCenterId)
        {
            CostCenter? costCenter = await context.CostCenters.FirstOrDefaultAsync(
                c => c.Id == costCenterId, token);

            if (costCenter is null)
            {
                problems.Add("Die Kostenstelle gibt es nicht.");
            }
            else if (!costCenter.IsActive)
            {
                problems.Add($"Die Kostenstelle „{costCenter.Label}“ ist stillgelegt.");
            }
        }

        if (task.Contacts.Count > 0)
        {
            List<Guid> ids = [.. task.Contacts.Select(c => c.ContactId).Distinct()];
            List<Contact> known = await context.Contacts
                .Where(c => ids.Contains(c.Id))
                .ToListAsync(token);

            if (known.Count < ids.Count)
            {
                problems.Add("Einen der beteiligten oder verknüpften Kontakte gibt es nicht.");
            }

            foreach (Contact inactive in known.Where(c => !c.IsActive))
            {
                problems.Add($"„{inactive.Name}“ ist im Adressbuch abgeschaltet.");
            }
        }

        if (task.ParentId is { } parentId)
        {
            TaskItem? parent = await context.TaskItems
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == parentId, token);

            if (parent is null)
            {
                problems.Add("Die übergeordnete Aufgabe gibt es nicht.");
            }
            else if (parent.ParentId is not null)
            {
                problems.Add(
                    $"Mehr als {MaxDepth} Ebenen gibt es nicht. Eine Checkliste in einer " +
                    "Checkliste versteht hinterher niemand mehr.");
            }
        }

        if (problems.Count > 0)
        {
            throw new TaskException(problems);
        }
    }
}
