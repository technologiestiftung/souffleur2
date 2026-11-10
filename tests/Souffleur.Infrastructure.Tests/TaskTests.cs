using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Accounting;
using Souffleur.Domain.Calendar;
using Souffleur.Domain.Catalogs;
using Souffleur.Domain.Contacts;
using Souffleur.Domain.Identity;
using Souffleur.Domain.Work;
using Souffleur.Infrastructure.Calendar;
using Souffleur.Infrastructure.Catalogs;
using Souffleur.Infrastructure.Work;

namespace Souffleur.Infrastructure.Tests;

/// <summary>
/// Aufgaben -- und die Stelle, an der sie den Kalender berühren: Eine Frist
/// gehört der Aufgabe und erscheint im Kalender, ohne dort zu liegen.
/// </summary>
public class TaskTests : AccountingTestBase
{
    private TaskService Service => new(Context, TimeProvider.System);

    private CalendarService Calendar => new(Context);

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<CatalogEntry> CategoryAsync(string key = "verwaltung")
    {
        var entry = new CatalogEntry
        {
            Catalog = CatalogKind.TaskCategory,
            Key = key,
            Label = "Verwaltung",
        };
        Context.CatalogEntries.Add(entry);
        await Context.SaveChangesAsync();
        return entry;
    }

    private async Task<Contact> PersonAsync(string surname = "Behrens")
    {
        var contact = new Contact
        {
            Kind = ContactKind.Person,
            Name = $"{surname}, Anja",
            GivenName = "Anja",
            Surname = surname,
            Roles = [new ContactRoleAssignment { Role = ContactRole.Staff }],
        };
        Context.Contacts.Add(contact);
        await Context.SaveChangesAsync();
        return contact;
    }

    private Task<TaskItem> AddAsync(
        string title = "Förderantrag",
        DateOnly? due = null,
        TaskState state = TaskState.Open,
        Guid? parentId = null) =>
        Service.AddAsync(new TaskItem
        {
            Title = title,
            DueDate = due,
            State = state,
            ParentId = parentId,
        });

    // --- Die Aufgabe selbst ---

    [Fact]
    public async Task Offene_Aufgaben_stehen_in_der_Liste_erledigte_nicht()
    {
        await AddAsync("Offen");
        await AddAsync("Fertig", state: TaskState.Done);

        (await Service.ListAsync()).Should().ContainSingle()
            .Which.Title.Should().Be("Offen");

        (await Service.ListAsync(includeClosed: true)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Was_eine_Frist_hat_steht_vorn_und_nach_ihr_sortiert()
    {
        await AddAsync("Ohne Frist");
        await AddAsync("Spät", due: Today.AddDays(30));
        await AddAsync("Bald", due: Today.AddDays(3));

        List<TaskItem> found = await Service.ListAsync();

        found.Select(t => t.Title).Should().ContainInOrder("Bald", "Spät", "Ohne Frist");
    }

    [Fact]
    public async Task Teilaufgaben_bleiben_bei_ihrer_Aufgabe()
    {
        // Die Premiere ist später dran als das Programmheft -- ihr Pressetext
        // aber früher. Nach reiner Fristsortierung risse das die Checkliste
        // auseinander.
        TaskItem premiere = await AddAsync("Premiere vorbereiten", due: Today.AddDays(40));
        await AddAsync("Pressetext", due: Today.AddDays(10), parentId: premiere.Id);
        await AddAsync("Programmheft", due: Today.AddDays(20));

        List<TaskItem> found = await Service.ListAsync();

        found.Select(t => t.Title).Should()
             .ContainInOrder("Premiere vorbereiten", "Pressetext", "Programmheft");
    }

    [Fact]
    public async Task Eine_Aufgabe_ohne_eigene_Frist_erbt_die_ihrer_Teilaufgaben()
    {
        TaskItem sammel = await AddAsync("Spielzeit vorbereiten");
        await AddAsync("Verträge", due: Today.AddDays(2), parentId: sammel.Id);
        await AddAsync("Etwas anderes", due: Today.AddDays(5));

        List<TaskItem> found = await Service.ListAsync();

        // Ohne das stünde die Sammelaufgabe hinten bei den fristlosen --
        // obwohl das Nächste in ihr steckt.
        found.Select(t => t.Title).Should()
             .ContainInOrder("Spielzeit vorbereiten", "Verträge", "Etwas anderes");
    }

    [Fact]
    public async Task Eine_Teilaufgabe_ohne_ihre_Aufgabe_steht_fuer_sich()
    {
        TaskItem parent = await AddAsync("Premiere vorbereiten", state: TaskState.Done);
        await AddAsync("Pressetext", due: Today.AddDays(3), parentId: parent.Id);

        // Die Aufgabe ist erledigt und damit weggefiltert. Die Teilaufgabe
        // darf deshalb nicht mitverschwinden.
        List<TaskItem> found = await Service.ListAsync();

        found.Should().ContainSingle().Which.Title.Should().Be("Pressetext");
    }

    [Fact]
    public async Task Erledigen_setzt_den_Zeitpunkt_und_Wiederoeffnen_nimmt_ihn_zurueck()
    {
        TaskItem task = await AddAsync();

        TaskItem done = await Service.SetStateAsync(task.Id, TaskState.Done);
        done.ClosedAt.Should().NotBeNull();

        TaskItem again = await Service.SetStateAsync(task.Id, TaskState.Open);
        again.ClosedAt.Should().BeNull();
    }

    [Fact]
    public async Task Ueberfaellig_ist_nur_was_offen_ist()
    {
        TaskItem late = await AddAsync("Verpasst", due: Today.AddDays(-3));
        late.IsOverdue(Today).Should().BeTrue();

        // Zu spät erledigt ist erledigt, nicht überfällig.
        TaskItem done = await Service.SetStateAsync(late.Id, TaskState.Done);
        done.IsOverdue(Today).Should().BeFalse();
    }

    [Fact]
    public async Task Die_Frist_liegt_nicht_vor_dem_Beginn()
    {
        Func<Task> create = () => Service.AddAsync(new TaskItem
        {
            Title = "Verdreht",
            StartDate = Today.AddDays(10),
            DueDate = Today.AddDays(2),
        });

        await create.Should().ThrowAsync<TaskException>().WithMessage("*vor dem Beginn*");
    }

    [Fact]
    public async Task Eine_Checkliste_geht_eine_Ebene_tief_nicht_zwei()
    {
        TaskItem parent = await AddAsync("Premiere vorbereiten");
        TaskItem child = await AddAsync("Pressetext", parentId: parent.Id);

        Func<Task> deeper = () => AddAsync("Foto aussuchen", parentId: child.Id);

        await deeper.Should().ThrowAsync<TaskException>().WithMessage("*Ebenen*");
    }

    [Fact]
    public async Task Eine_Aufgabe_mit_Teilaufgaben_laesst_sich_nicht_loeschen()
    {
        TaskItem parent = await AddAsync("Premiere vorbereiten");
        await AddAsync("Pressetext", parentId: parent.Id);

        Func<Task> delete = () => Service.DeleteAsync(parent.Id);

        await delete.Should().ThrowAsync<TaskException>().WithMessage("*Teilaufgaben*");
    }

    [Fact]
    public async Task Eine_abgeschaltete_Kategorie_laesst_sich_nicht_mehr_waehlen()
    {
        CatalogEntry category = await CategoryAsync();
        await new CatalogService(Context).SetActiveAsync(category.Id, false);

        Func<Task> create = () => Service.AddAsync(new TaskItem
        {
            Title = "Mit toter Kategorie",
            CategoryId = category.Id,
        });

        await create.Should().ThrowAsync<TaskException>().WithMessage("*abgeschaltet*");
    }

    [Fact]
    public async Task Eine_Kategorie_die_verwendet_wird_laesst_sich_nicht_loeschen()
    {
        CatalogEntry category = await CategoryAsync();
        await Service.AddAsync(new TaskItem { Title = "Antrag", CategoryId = category.Id });

        Func<Task> delete = () => new CatalogService(Context).DeleteAsync(category.Id);

        await delete.Should().ThrowAsync<CatalogException>().WithMessage("*verwendet*");
    }

    [Fact]
    public async Task Nach_Zustaendigkeit_laesst_sich_filtern()
    {
        Contact anja = await PersonAsync();
        await Service.AddAsync(new TaskItem { Title = "Ihre", AssigneeId = anja.Id });
        await AddAsync("Niemandes");

        List<TaskItem> hers = await Service.ListAsync(assigneeId: anja.Id);

        hers.Should().ContainSingle().Which.Title.Should().Be("Ihre");
    }

    // --- Beteiligte, Kontakte, Kostenstelle ---

    [Fact]
    public async Task Beteiligte_kommen_dazu_und_gehen_wieder()
    {
        Contact anja = await PersonAsync();
        Contact ben = await PersonAsync("Schröder");
        var task = new TaskItem { Title = "Abnahme Bühnenbild" };
        Service.SetContacts(task, participants: [anja.Id, ben.Id], related: []);
        await Service.AddAsync(task);

        TaskItem reloaded = (await Service.FindAsync(task.Id))!;
        reloaded.Participants.Select(p => p.ContactId).Should().BeEquivalentTo([anja.Id, ben.Id]);
        reloaded.RelatedContacts.Should().BeEmpty();

        // Ben geht, dafuer wird Anja zusaetzlich verknuepft: je Art nur einmal.
        await Service.UpdateAsync(task.Id, t => Service.SetContacts(t, [anja.Id], [anja.Id]));

        Context.ChangeTracker.Clear();
        reloaded = (await Service.FindAsync(task.Id))!;
        reloaded.Participants.Select(p => p.ContactId).Should().Equal(anja.Id);
        reloaded.RelatedContacts.Select(p => p.ContactId).Should().Equal(anja.Id);
        (await Context.TaskContacts.CountAsync(c => c.TaskId == task.Id)).Should().Be(2);
    }

    [Fact]
    public async Task Ein_abgeschalteter_Kontakt_wird_nicht_beteiligt()
    {
        Contact weg = await PersonAsync("Fort");
        weg.IsActive = false;
        await Context.SaveChangesAsync();
        var task = new TaskItem { Title = "Probe" };
        Service.SetContacts(task, [weg.Id], []);

        Func<Task> act = () => Service.AddAsync(task);

        (await act.Should().ThrowAsync<TaskException>())
            .Which.Problems.Should().Contain(p => p.Contains("abgeschaltet"));
    }

    [Fact]
    public async Task Die_Frist_steht_auch_im_Kalender_der_Beteiligten()
    {
        Contact anja = await PersonAsync();
        Contact ben = await PersonAsync("Schröder");
        var task = new TaskItem { Title = "Pressetext", DueDate = Today, AssigneeId = anja.Id };
        Service.SetContacts(task, [ben.Id], []);
        await Service.AddAsync(task);

        List<CalendarOccurrence> occurrences = await Calendar.RangeAsync(Today, Today);

        CalendarOccurrence frist = occurrences.Should().ContainSingle(o => o.Source == CalendarSource.Tasks).Subject;
        frist.Participants.Select(p => p.ContactId).Should().BeEquivalentTo([anja.Id, ben.Id]);
        frist.Participants.Should().Contain(p => p.ContactId == ben.Id && p.Function == "Beteiligt");
    }

    [Fact]
    public async Task Eine_Aufgabe_haengt_an_einer_Kostenstelle_aber_nicht_an_einer_stillgelegten()
    {
        var marketing = new CostCenter { Number = "200", Name = "Marketing" };
        var alt = new CostCenter { Number = "900", Name = "Altlast", IsActive = false };
        Context.CostCenters.AddRange(marketing, alt);
        await Context.SaveChangesAsync();

        TaskItem task = await Service.AddAsync(new TaskItem { Title = "Plakat", CostCenterId = marketing.Id });
        (await Service.FindAsync(task.Id))!.CostCenter!.Label.Should().Be("200 Marketing");

        Func<Task> act = () => Service.AddAsync(new TaskItem { Title = "Flyer", CostCenterId = alt.Id });
        (await act.Should().ThrowAsync<TaskException>())
            .Which.Problems.Should().Contain(p => p.Contains("stillgelegt"));
    }

    // --- Gruppen ---

    private async Task<Group> GruppeAsync(string name = "Technik", bool aktiv = true)
    {
        var gruppe = new Group { Name = name, IsActive = aktiv };
        Context.Groups.Add(gruppe);
        await Context.SaveChangesAsync();
        return gruppe;
    }

    [Fact]
    public async Task Eine_Aufgabe_laesst_sich_einer_Gruppe_geben_und_nach_ihr_filtern()
    {
        Group technik = await GruppeAsync();
        Group maske = await GruppeAsync("Maske");
        await Service.AddAsync(new TaskItem { Title = "Leiter prüfen", GroupId = technik.Id });
        await Service.AddAsync(new TaskItem { Title = "Perücken", GroupId = maske.Id });
        await AddAsync("Ohne Gruppe");

        List<TaskItem> found = await Service.ListAsync(groupIds: [technik.Id]);

        found.Should().ContainSingle().Which.Title.Should().Be("Leiter prüfen");
    }

    [Fact]
    public async Task Wer_in_keiner_Gruppe_ist_bekommt_ueber_Gruppen_nichts()
    {
        Group technik = await GruppeAsync();
        await Service.AddAsync(new TaskItem { Title = "Leiter prüfen", GroupId = technik.Id });

        // Leer heisst „in keiner Gruppe", nicht „nicht gefiltert".
        (await Service.ListAsync(groupIds: [])).Should().BeEmpty();
    }

    [Fact]
    public async Task Eine_stillgelegte_Gruppe_bekommt_keine_neuen_Aufgaben()
    {
        Group alt = await GruppeAsync("Alte Werkstatt", aktiv: false);

        Func<Task> create = () => Service.AddAsync(new TaskItem { Title = "Hobeln", GroupId = alt.Id });

        await create.Should().ThrowAsync<TaskException>().WithMessage("*stillgelegt*");
    }

    [Fact]
    public async Task Wird_die_Gruppe_geloescht_bleibt_die_Aufgabe_ohne_Gruppe()
    {
        Group technik = await GruppeAsync();
        TaskItem aufgabe = await Service.AddAsync(new TaskItem { Title = "Leiter prüfen", GroupId = technik.Id });

        Context.Groups.Remove(technik);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        TaskItem? danach = await Service.FindAsync(aufgabe.Id);
        danach.Should().NotBeNull();
        danach!.GroupId.Should().BeNull();
    }

    // --- Der Zeitstrahl ---

    [Fact]
    public async Task Nur_was_einen_Anfang_oder_eine_Frist_hat_steht_auf_dem_Zeitstrahl()
    {
        await Service.AddAsync(new TaskItem
        {
            Title = "Mit Dauer",
            StartDate = Today,
            DueDate = Today.AddDays(10),
        });
        await AddAsync("Nur Frist", due: Today.AddDays(4));
        await AddAsync("Ohne alles");

        Timeline timeline = await Service.TimelineAsync(Today, Today.AddDays(30));

        timeline.Tasks.Select(t => t.Title).Should().BeEquivalentTo("Mit Dauer", "Nur Frist");

        // Verschwiegen wird das Übrige nicht.
        timeline.Unscheduled.Should().Be(1);
    }

    [Fact]
    public async Task Ein_Balken_der_in_den_Zeitraum_hineinragt_erscheint()
    {
        await Service.AddAsync(new TaskItem
        {
            Title = "Lange Produktion",
            StartDate = Today.AddDays(-30),
            DueDate = Today.AddDays(30),
        });

        // Der Blick in die Mitte eines Vorgangs darf ihn nicht verlieren.
        Timeline timeline = await Service.TimelineAsync(Today, Today.AddDays(3));

        timeline.Tasks.Should().ContainSingle();
    }

    [Fact]
    public async Task Eine_verdrehte_Reihenfolge_wird_gemeldet()
    {
        TaskItem buehne = await Service.AddAsync(new TaskItem
        {
            Title = "Bühnenbild abnehmen",
            StartDate = Today.AddDays(5),
            DueDate = Today.AddDays(10),
        });

        TaskItem presse = await AddAsync("Pressetext", due: Today.AddDays(7));
        await Service.LinkAsync(presse.Id, buehne.Id);

        Timeline timeline = await Service.TimelineAsync(Today, Today.AddDays(30));

        // Der Pressetext ist vor der Abnahme fällig, auf die er wartet.
        DependencyBreach breach = timeline.Breaches.Should().ContainSingle().Subject;
        breach.TaskTitle.Should().Be("Pressetext");
        breach.DependsOnTitle.Should().Be("Bühnenbild abnehmen");
    }

    [Fact]
    public async Task Eine_Reihenfolge_die_aufgeht_wird_nicht_gemeldet()
    {
        TaskItem erst = await Service.AddAsync(new TaskItem
        {
            Title = "Zuerst",
            StartDate = Today,
            DueDate = Today.AddDays(5),
        });

        TaskItem dann = await Service.AddAsync(new TaskItem
        {
            Title = "Danach",
            StartDate = Today.AddDays(5),
            DueDate = Today.AddDays(9),
        });

        await Service.LinkAsync(dann.Id, erst.Id);

        Timeline timeline = await Service.TimelineAsync(Today, Today.AddDays(30));

        // Derselbe Tag ist in Ordnung: vormittags abbauen, nachmittags
        // einrichten.
        timeline.Breaches.Should().BeEmpty();
        timeline.Dependencies.Should().ContainSingle();
    }

    [Fact]
    public async Task Ein_Kreis_laesst_sich_nicht_knuepfen()
    {
        TaskItem a = await AddAsync("A", due: Today.AddDays(5));
        TaskItem b = await AddAsync("B", due: Today.AddDays(6));
        TaskItem c = await AddAsync("C", due: Today.AddDays(7));

        await Service.LinkAsync(b.Id, a.Id);
        await Service.LinkAsync(c.Id, b.Id);

        // A wartet auf C, C auf B, B auf A -- keine könnte je beginnen.
        Func<Task> circle = () => Service.LinkAsync(a.Id, c.Id);

        await circle.Should().ThrowAsync<TaskException>().WithMessage("*Kreis*");
    }

    [Fact]
    public async Task Eine_Aufgabe_auf_die_gewartet_wird_laesst_sich_nicht_loeschen()
    {
        TaskItem erst = await AddAsync("Zuerst", due: Today.AddDays(3));
        TaskItem dann = await AddAsync("Danach", due: Today.AddDays(9));
        await Service.LinkAsync(dann.Id, erst.Id);

        Func<Task> delete = () => Service.DeleteAsync(erst.Id);

        await delete.Should().ThrowAsync<TaskException>().WithMessage("*warten*");
    }

    [Fact]
    public async Task Ein_Pfeil_ins_Nichts_wird_nicht_gezeichnet()
    {
        TaskItem sichtbar = await AddAsync("Sichtbar", due: Today.AddDays(3));
        TaskItem fern = await AddAsync("Weit weg", due: Today.AddDays(300));
        await Service.LinkAsync(sichtbar.Id, fern.Id);

        Timeline timeline = await Service.TimelineAsync(Today, Today.AddDays(30));

        // Die Verbindung gibt es, aber nur ein Ende ist zu sehen.
        timeline.Tasks.Should().ContainSingle();
        timeline.Dependencies.Should().BeEmpty();
    }

    // --- Wo die Aufgaben den Kalender berühren ---

    [Fact]
    public async Task Eine_Frist_erscheint_im_Kalender_ohne_dort_zu_liegen()
    {
        DateOnly due = Today.AddDays(5);
        TaskItem task = await AddAsync("Förderantrag einreichen", due: due);

        List<CalendarOccurrence> found = await Calendar.RangeAsync(due, due);

        CalendarOccurrence entry = found.Should().ContainSingle().Subject;
        entry.Title.Should().Be("Förderantrag einreichen");
        entry.IsAllDay.Should().BeTrue();

        // Sie gehört den Aufgaben: kein Termin dahinter, aber auffindbar.
        entry.Source.Should().Be(CalendarSource.Tasks);
        entry.EventId.Should().BeNull();
        entry.SourceId.Should().Be(task.Id);

        (await Context.CalendarEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Erledigte_Fristen_verschwinden_aus_dem_Kalender()
    {
        DateOnly due = Today.AddDays(5);
        TaskItem task = await AddAsync("Antrag", due: due);

        (await Calendar.RangeAsync(due, due)).Should().ContainSingle();

        await Service.SetStateAsync(task.Id, TaskState.Done);

        // Was getan ist, schränkt die Planung nicht mehr ein.
        (await Calendar.RangeAsync(due, due)).Should().BeEmpty();
    }

    [Fact]
    public async Task Die_Zustaendigkeit_zaehlt_im_Kalender_wie_eine_Beteiligung()
    {
        Contact anja = await PersonAsync();
        DateOnly due = Today.AddDays(5);

        await Service.AddAsync(new TaskItem
        {
            Title = "Antrag",
            DueDate = due,
            AssigneeId = anja.Id,
        });

        // „Was habe ich diese Woche?" umfasst Termine und Fristen.
        List<CalendarOccurrence> hers = await Calendar.RangeAsync(due, due, people: [anja.Id]);

        hers.Should().ContainSingle()
            .Which.Participants.Should().ContainSingle()
            .Which.Function.Should().Be("Zuständig");
    }
}
