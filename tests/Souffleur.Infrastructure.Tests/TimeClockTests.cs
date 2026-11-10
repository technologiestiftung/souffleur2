using FluentAssertions;
using Souffleur.Domain.Contacts;
using Souffleur.Domain.Personnel;
using Souffleur.Infrastructure.Personnel;

namespace Souffleur.Infrastructure.Tests;

/// <summary>
/// Kommen und Gehen -- und die Regeln, die verhindern, dass ein Saldo entsteht,
/// den niemand mehr nachrechnen kann.
/// </summary>
public class TimeClockTests : AccountingTestBase
{
    private TimeClockService Service => new(Context, TimeProvider.System);

    private async Task<Contact> PersonAsync(string surname = "Krause")
    {
        var contact = new Contact
        {
            Kind = ContactKind.Person,
            Name = $"{surname}, Anna",
            GivenName = "Anna",
            Surname = surname,
            Roles = [new ContactRoleAssignment { Role = ContactRole.Staff }],
        };

        Context.Contacts.Add(contact);
        await Context.SaveChangesAsync();
        return contact;
    }

    [Fact]
    public async Task Kommen_legt_eine_laufende_Zeit_an()
    {
        Contact person = await PersonAsync();

        TimeEntry entry = await Service.ClockInAsync(person.Id);

        entry.IsRunning.Should().BeTrue();
        entry.Source.Should().Be(TimeEntrySource.Clock);
        (await Service.RunningAsync(person.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task Zweimal_kommen_geht_nicht()
    {
        Contact person = await PersonAsync();
        await Service.ClockInAsync(person.Id);

        Func<Task> again = () => Service.ClockInAsync(person.Id);

        await again.Should().ThrowAsync<TimeEntryException>()
            .WithMessage("*läuft schon eine Zeit*");
    }

    [Fact]
    public async Task Gehen_ohne_Kommen_geht_nicht()
    {
        Contact person = await PersonAsync();

        Func<Task> leave = () => Service.ClockOutAsync(person.Id);

        await leave.Should().ThrowAsync<TimeEntryException>()
            .WithMessage("*keine Zeit*");
    }

    [Fact]
    public async Task Gehen_schliesst_die_Zeit_und_zieht_die_Pause_ab()
    {
        Contact person = await PersonAsync();
        await Service.ClockInAsync(person.Id);

        TimeEntry entry = await Service.ClockOutAsync(person.Id, breakMinutes: 0);

        entry.IsRunning.Should().BeFalse();
        entry.WorkedMinutes.Should().Be(0);
        (await Service.RunningAsync(person.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Eine_laufende_Zeit_laesst_sich_verwerfen()
    {
        Contact person = await PersonAsync();
        await Service.ClockInAsync(person.Id);

        await Service.DiscardRunningAsync(person.Id);

        (await Service.RunningAsync(person.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Eine_nachgetragene_Zeit_braucht_ein_Ende()
    {
        Contact person = await PersonAsync();

        Func<Task> add = () => Service.AddAsync(
            new TimeEntryInput(person.Id, Morning, null, 0, null, null, null));

        await add.Should().ThrowAsync<TimeEntryException>()
            .WithMessage("*braucht ein Ende*");
    }

    [Fact]
    public async Task Nachgetragene_Zeiten_bleiben_als_solche_erkennbar()
    {
        Contact person = await PersonAsync();

        TimeEntry entry = await Service.AddAsync(
            new TimeEntryInput(person.Id, Morning, Morning.AddHours(4), 0, null, null, "Gastspiel"));

        entry.Source.Should().Be(TimeEntrySource.Manual);
    }

    [Fact]
    public async Task Niemand_arbeitet_zweimal_zur_selben_Zeit()
    {
        Contact person = await PersonAsync();
        await Service.AddAsync(
            new TimeEntryInput(person.Id, Morning, Morning.AddHours(4), 0, null, null, null));

        Func<Task> overlapping = () => Service.AddAsync(
            new TimeEntryInput(person.Id, Morning.AddHours(2), Morning.AddHours(6), 0, null, null, null));

        await overlapping.Should().ThrowAsync<TimeEntryException>()
            .WithMessage("*steht schon eine Zeit*");
    }

    [Fact]
    public async Task Zwei_Personen_duerfen_zur_selben_Zeit_arbeiten()
    {
        Contact one = await PersonAsync();
        Contact other = await PersonAsync("Schröder");

        await Service.AddAsync(
            new TimeEntryInput(one.Id, Morning, Morning.AddHours(4), 0, null, null, null));

        Func<Task> second = () => Service.AddAsync(
            new TimeEntryInput(other.Id, Morning, Morning.AddHours(4), 0, null, null, null));

        await second.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Eine_Korrektur_ohne_Grund_wird_abgewiesen()
    {
        Contact person = await PersonAsync();
        TimeEntry entry = await Service.AddAsync(
            new TimeEntryInput(person.Id, Morning, Morning.AddHours(4), 0, null, null, null));

        Func<Task> correct = () => Service.CorrectAsync(
            entry.Id,
            new TimeEntryInput(person.Id, Morning, Morning.AddHours(5), 0, null, null, null),
            reason: "   ");

        await correct.Should().ThrowAsync<TimeEntryException>()
            .WithMessage("*braucht einen Grund*");
    }

    [Fact]
    public async Task Eine_Korrektur_mit_Grund_bleibt_am_Eintrag_stehen()
    {
        Contact person = await PersonAsync();
        TimeEntry entry = await Service.AddAsync(
            new TimeEntryInput(person.Id, Morning, Morning.AddHours(4), 0, null, null, null));

        TimeEntry corrected = await Service.CorrectAsync(
            entry.Id,
            new TimeEntryInput(person.Id, Morning, Morning.AddHours(5), 30, null, null, null),
            reason: "Gehen vergessen");

        corrected.CorrectionReason.Should().Be("Gehen vergessen");
        corrected.WorkedMinutes.Should().Be(270);
    }

    [Fact]
    public async Task Eine_laufende_Zeit_laesst_sich_nicht_richtigstellen()
    {
        Contact person = await PersonAsync();
        TimeEntry running = await Service.ClockInAsync(person.Id);

        Func<Task> correct = () => Service.CorrectAsync(
            running.Id,
            new TimeEntryInput(person.Id, Morning, Morning.AddHours(1), 0, null, null, null),
            reason: "Tippfehler");

        await correct.Should().ThrowAsync<TimeEntryException>()
            .WithMessage("*läuft noch*");
    }

    [Fact]
    public async Task Ein_Konto_findet_seinen_Kontakt()
    {
        Contact person = await PersonAsync();
        Guid account = Guid.CreateVersion7();
        person.UserId = account;
        await Context.SaveChangesAsync();

        (await Service.ContactOfAsync(account))!.Id.Should().Be(person.Id);
    }

    private static readonly DateTimeOffset Morning =
        new(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(1));
}
