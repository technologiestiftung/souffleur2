using FluentAssertions;
using Souffleur.Domain.Calendar;
using Souffleur.Domain.Catalogs;
using Souffleur.Domain.Contacts;
using Souffleur.Domain.Identity;
using Souffleur.Domain.Personnel;
using Souffleur.Infrastructure.Calendar;
using Souffleur.Infrastructure.Personnel;

namespace Souffleur.Infrastructure.Tests;

/// <summary>
/// Der Dienstplan: Bedarf je Terminart, gezaehlt gegen die Einteilung.
/// </summary>
public class DutyPlanTests : AccountingTestBase
{
    private DutyPlanService Service => new(Context, new CalendarService(Context));

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<CatalogEntry> EntryAsync(CatalogKind kind, string key, string label)
    {
        var entry = new CatalogEntry { Catalog = kind, Key = key, Label = label };
        Context.CatalogEntries.Add(entry);
        await Context.SaveChangesAsync();
        return entry;
    }

    private async Task<Contact> PersonAsync(string surname)
    {
        var contact = new Contact
        {
            Kind = ContactKind.Person,
            Name = $"{surname}, Ben",
            GivenName = "Ben",
            Surname = surname,
            Roles = [new ContactRoleAssignment { Role = ContactRole.Staff }],
        };

        Context.Contacts.Add(contact);
        await Context.SaveChangesAsync();
        return contact;
    }

    private async Task<CalendarEvent> EventAsync(
        CatalogEntry category,
        params EventParticipant[] participants)
    {
        DateTime day = DateTime.UtcNow.Date.AddDays(3);

        var calendarEvent = new CalendarEvent
        {
            Title = "Vorstellung Hamlet",
            CategoryId = category.Id,
            StartsAt = new DateTimeOffset(day.AddHours(19), TimeSpan.Zero),
            EndsAt = new DateTimeOffset(day.AddHours(22), TimeSpan.Zero),
            Participants = [.. participants],
        };

        Context.CalendarEvents.Add(calendarEvent);
        await Context.SaveChangesAsync();
        return calendarEvent;
    }

    [Fact]
    public async Task Ein_Termin_ohne_Bedarf_und_ohne_Einteilung_steht_nicht_im_Plan()
    {
        CatalogEntry category = await EntryAsync(CatalogKind.EventCategory, "probe", "Probe");
        await EventAsync(category);

        (await Service.PlanAsync(Today, Today.AddDays(28))).Should().BeEmpty();
    }

    [Fact]
    public async Task Eine_Vorstellung_ohne_jede_Einteilung_faellt_auf()
    {
        CatalogEntry category = await EntryAsync(CatalogKind.EventCategory, "vorstellung", "Vorstellung");
        CatalogEntry technik = await EntryAsync(CatalogKind.ParticipationRole, "technik", "Technik");
        await Service.SaveAsync(category.Id, [(technik.Id, 2)]);
        await EventAsync(category);

        List<PlannedDuty> plan = await Service.PlanAsync(Today, Today.AddDays(28));

        plan.Should().ContainSingle();
        plan[0].Open.Should().Be(2);
        plan[0].Slots.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Required = 2, Assigned = 0, Open = 2 });
    }

    [Fact]
    public async Task Die_Gruppe_am_Bedarf_steht_an_der_offenen_Stelle()
    {
        CatalogEntry category = await EntryAsync(CatalogKind.EventCategory, "vorstellung", "Vorstellung");
        CatalogEntry licht = await EntryAsync(CatalogKind.ParticipationRole, "licht", "Beleuchtung");
        var technik = new Group { Name = "Technik" };
        Context.Groups.Add(technik);
        await Context.SaveChangesAsync();

        await Service.SaveWithGroupsAsync(category.Id, [(licht.Id, 1, technik.Id)]);
        await EventAsync(category);

        DutySlot slot = (await Service.PlanAsync(Today, Today.AddDays(28)))
            .Should().ContainSingle().Which.Slots.Should().ContainSingle().Subject;

        slot.GroupId.Should().Be(technik.Id);
        slot.GroupName.Should().Be("Technik");
    }

    [Fact]
    public async Task Eingeteilte_zaehlen_gegen_den_Bedarf()
    {
        CatalogEntry category = await EntryAsync(CatalogKind.EventCategory, "vorstellung", "Vorstellung");
        CatalogEntry technik = await EntryAsync(CatalogKind.ParticipationRole, "technik", "Technik");
        await Service.SaveAsync(category.Id, [(technik.Id, 2)]);

        Contact one = await PersonAsync("Schröder");
        await EventAsync(category, new EventParticipant
        {
            ContactId = one.Id,
            FunctionId = technik.Id,
            Status = ParticipationStatus.Accepted,
        });

        List<PlannedDuty> plan = await Service.PlanAsync(Today, Today.AddDays(28));

        plan[0].Slots[0].Assigned.Should().Be(1);
        plan[0].Open.Should().Be(1);
    }

    [Fact]
    public async Task Wer_abgesagt_hat_besetzt_nichts()
    {
        CatalogEntry category = await EntryAsync(CatalogKind.EventCategory, "vorstellung", "Vorstellung");
        CatalogEntry technik = await EntryAsync(CatalogKind.ParticipationRole, "technik", "Technik");
        await Service.SaveAsync(category.Id, [(technik.Id, 1)]);

        Contact one = await PersonAsync("Schröder");
        await EventAsync(category, new EventParticipant
        {
            ContactId = one.Id,
            FunctionId = technik.Id,
            Status = ParticipationStatus.Declined,
        });

        (await Service.PlanAsync(Today, Today.AddDays(28)))[0].Open.Should().Be(1);
    }

    [Fact]
    public async Task Mehr_Eingeteilte_als_noetig_sind_kein_Fehlbestand()
    {
        CatalogEntry category = await EntryAsync(CatalogKind.EventCategory, "vorstellung", "Vorstellung");
        CatalogEntry technik = await EntryAsync(CatalogKind.ParticipationRole, "technik", "Technik");
        await Service.SaveAsync(category.Id, [(technik.Id, 1)]);

        Contact one = await PersonAsync("Schröder");
        Contact two = await PersonAsync("Yilmaz");
        await EventAsync(
            category,
            new EventParticipant { ContactId = one.Id, FunctionId = technik.Id },
            new EventParticipant { ContactId = two.Id, FunctionId = technik.Id });

        PlannedDuty duty = (await Service.PlanAsync(Today, Today.AddDays(28)))[0];

        duty.Slots[0].Assigned.Should().Be(2);
        duty.Open.Should().Be(0);
    }

    [Fact]
    public async Task Je_Terminart_gibt_es_genau_einen_Bedarf()
    {
        CatalogEntry category = await EntryAsync(CatalogKind.EventCategory, "vorstellung", "Vorstellung");
        CatalogEntry technik = await EntryAsync(CatalogKind.ParticipationRole, "technik", "Technik");
        CatalogEntry einlass = await EntryAsync(CatalogKind.ParticipationRole, "einlass", "Einlass");

        await Service.SaveAsync(category.Id, [(technik.Id, 2)]);
        await Service.SaveAsync(category.Id, [(technik.Id, 1), (einlass.Id, 1)]);

        List<DutyTemplate> templates = await Service.TemplatesAsync();

        templates.Should().ContainSingle();
        templates[0].Requirements.Should().HaveCount(2);
    }

    [Fact]
    public async Task Ein_Bedarf_ohne_Funktionen_wird_abgewiesen()
    {
        CatalogEntry category = await EntryAsync(CatalogKind.EventCategory, "vorstellung", "Vorstellung");

        Func<Task> empty = () => Service.SaveAsync(category.Id, []);

        await empty.Should().ThrowAsync<DutyPlanException>()
            .WithMessage("*sagt nichts aus*");
    }

    [Fact]
    public async Task Dieselbe_Funktion_zweimal_wird_abgewiesen()
    {
        CatalogEntry category = await EntryAsync(CatalogKind.EventCategory, "vorstellung", "Vorstellung");
        CatalogEntry technik = await EntryAsync(CatalogKind.ParticipationRole, "technik", "Technik");

        Func<Task> twice = () => Service.SaveAsync(category.Id, [(technik.Id, 1), (technik.Id, 2)]);

        await twice.Should().ThrowAsync<DutyPlanException>()
            .WithMessage("*zweimal*");
    }

    [Fact]
    public async Task Vom_Dienstplan_aus_laesst_sich_jemand_einteilen()
    {
        CatalogEntry category = await EntryAsync(CatalogKind.EventCategory, "vorstellung", "Vorstellung");
        CatalogEntry technik = await EntryAsync(CatalogKind.ParticipationRole, "technik", "Technik");
        await Service.SaveAsync(category.Id, [(technik.Id, 1)]);

        CalendarEvent evening = await EventAsync(category);
        Contact one = await PersonAsync("Schröder");

        var calendar = new CalendarService(Context);
        await calendar.AssignAsync(
            evening.Id,
            new ParticipantInput(one.Id, technik.Id, ParticipationStatus.Assigned));

        PlannedDuty duty = (await Service.PlanAsync(Today, Today.AddDays(28)))[0];

        duty.Open.Should().Be(0);
        duty.Assigned.Should().ContainSingle().Which.Name.Should().Be("Schröder, Ben");
    }

    [Fact]
    public async Task Dieselbe_Person_zweimal_am_selben_Termin_geht_nicht()
    {
        CatalogEntry category = await EntryAsync(CatalogKind.EventCategory, "vorstellung", "Vorstellung");
        CatalogEntry technik = await EntryAsync(CatalogKind.ParticipationRole, "technik", "Technik");
        CalendarEvent evening = await EventAsync(category);
        Contact one = await PersonAsync("Schröder");

        var calendar = new CalendarService(Context);
        await calendar.AssignAsync(evening.Id, new ParticipantInput(one.Id, technik.Id));

        Func<Task> again = () => calendar.AssignAsync(
            evening.Id, new ParticipantInput(one.Id, technik.Id));

        await again.Should().ThrowAsync<CalendarException>()
            .WithMessage("*schon eingeteilt*");
    }

    [Fact]
    public async Task Eine_Einteilung_laesst_sich_wieder_loesen()
    {
        CatalogEntry category = await EntryAsync(CatalogKind.EventCategory, "vorstellung", "Vorstellung");
        CatalogEntry technik = await EntryAsync(CatalogKind.ParticipationRole, "technik", "Technik");
        await Service.SaveAsync(category.Id, [(technik.Id, 1)]);

        CalendarEvent evening = await EventAsync(category);
        Contact one = await PersonAsync("Schröder");

        var calendar = new CalendarService(Context);
        await calendar.AssignAsync(evening.Id, new ParticipantInput(one.Id, technik.Id));
        await calendar.UnassignAsync(evening.Id, one.Id);

        (await Service.PlanAsync(Today, Today.AddDays(28)))[0].Open.Should().Be(1);
    }

    [Fact]
    public async Task Ein_geloeschter_Bedarf_laesst_den_Termin_in_Ruhe()
    {
        CatalogEntry category = await EntryAsync(CatalogKind.EventCategory, "vorstellung", "Vorstellung");
        CatalogEntry technik = await EntryAsync(CatalogKind.ParticipationRole, "technik", "Technik");
        await Service.SaveAsync(category.Id, [(technik.Id, 2)]);
        await EventAsync(category);

        await Service.RemoveAsync(category.Id);

        (await Service.PlanAsync(Today, Today.AddDays(28))).Should().BeEmpty();
    }
}
