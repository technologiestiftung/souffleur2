using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Calendar;
using Souffleur.Domain.Catalogs;
using Souffleur.Domain.Contacts;
using Souffleur.Infrastructure.Calendar;
using Souffleur.Infrastructure.Catalogs;

namespace Souffleur.Infrastructure.Tests;

/// <summary>
/// Wer bei einem Termin dabei ist -- und was passiert, wenn jemand zur selben
/// Zeit an zwei Stellen stehen soll. Niemand ist an zwei Orten; deshalb läuft
/// die Prüfung für Leute wie für Räume.
/// </summary>
public class ParticipantTests : AccountingTestBase
{
    private CalendarService Service => new(Context);

    private static DateOnly Monday
    {
        get
        {
            DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
            int ahead = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
            return today.AddDays(ahead == 0 ? 7 : ahead);
        }
    }

    private static DateTimeOffset At(DateOnly date, int hour, int minute = 0) =>
        EventSeries.ToInstant(date, new TimeOnly(hour, minute), TimeZoneInfo.Utc);

    private async Task<Contact> PersonAsync(string surname, string given = "Test")
    {
        var contact = new Contact
        {
            Kind = ContactKind.Person,
            Name = $"{surname}, {given}",
            GivenName = given,
            Surname = surname,
            Roles = [new ContactRoleAssignment { Role = ContactRole.Artist }],
        };

        Context.Contacts.Add(contact);
        await Context.SaveChangesAsync();
        return contact;
    }

    private async Task<CatalogEntry> FunctionAsync(string key, string label)
    {
        var entry = new CatalogEntry
        {
            Catalog = CatalogKind.ParticipationRole,
            Key = key,
            Label = label,
        };

        Context.CatalogEntries.Add(entry);
        await Context.SaveChangesAsync();
        return entry;
    }

    private Task<CalendarEvent> EveningAsync(
        IReadOnlyCollection<ParticipantInput> cast,
        int dayOffset = 0,
        int hour = 20,
        int hours = 2,
        string title = "Vorstellung",
        bool force = false)
    {
        DateOnly day = Monday.AddDays(dayOffset);

        return Service.AddEventAsync(
            new CalendarEvent
            {
                Title = title,
                StartsAt = At(day, hour),
                EndsAt = At(day, hour) + TimeSpan.FromHours(hours),
            },
            participants: cast,
            force: force);
    }

    [Fact]
    public async Task Eine_Person_steht_am_Termin_und_erscheint_im_Zeitraum()
    {
        Contact clara = await PersonAsync("Weber", "Clara");
        CatalogEntry ensemble = await FunctionAsync("ensemble", "Ensemble");

        await EveningAsync([new ParticipantInput(clara.Id, ensemble.Id)], title: "Hamlet");

        List<CalendarOccurrence> found = await Service.RangeAsync(Monday, Monday);

        OccurrenceParticipant person = found.Should().ContainSingle()
            .Which.Participants.Should().ContainSingle().Subject;

        person.Name.Should().Be("Weber, Clara");
        person.Function.Should().Be("Ensemble");
        person.Status.Should().Be(ParticipationStatus.Assigned);
    }

    [Fact]
    public async Task Dieselbe_Person_zur_selben_Zeit_an_zwei_Stellen_wird_gemeldet()
    {
        Contact clara = await PersonAsync("Weber", "Clara");

        await EveningAsync([new ParticipantInput(clara.Id)], hour: 19, hours: 3, title: "Probe");

        Func<Task> second = () =>
            EveningAsync([new ParticipantInput(clara.Id)], hour: 20, title: "Vorstellung");

        ScheduleConflict conflict = (await second.Should().ThrowAsync<ScheduleConflictException>())
            .Which.Conflicts.Should().ContainSingle().Subject;

        conflict.Subject.Should().Be(ConflictSubject.Person);
        conflict.SubjectName.Should().Be("Weber, Clara");
        conflict.Title.Should().Be("Probe");
    }

    [Fact]
    public async Task Wer_abgesagt_hat_ist_frei()
    {
        Contact clara = await PersonAsync("Weber", "Clara");

        await EveningAsync(
            [new ParticipantInput(clara.Id, Status: ParticipationStatus.Declined)],
            hour: 19,
            hours: 3,
            title: "Probe");

        // Eine Absage ist keine halbe Einteilung -- sie gibt die Person frei.
        await EveningAsync([new ParticipantInput(clara.Id)], hour: 20, title: "Vorstellung");

        (await Context.EventParticipants.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Eine_andere_Person_steht_nicht_im_Weg()
    {
        Contact clara = await PersonAsync("Weber", "Clara");
        Contact ben = await PersonAsync("Schröder", "Ben");

        await EveningAsync([new ParticipantInput(clara.Id)], hour: 19, hours: 3);
        await EveningAsync([new ParticipantInput(ben.Id)], hour: 20, title: "Parallel");

        (await Context.EventParticipants.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Eine_Serie_besetzt_jeden_ihrer_Termine()
    {
        Contact clara = await PersonAsync("Weber", "Clara");

        await Service.AddSeriesAsync(
            new EventSeries
            {
                Title = "Probe Hamlet",
                StartDate = Monday,
                StartTime = new TimeOnly(19, 0),
                DurationMinutes = 180,
                TimeZoneId = "UTC",
                Frequency = RecurrenceFrequency.Weekly,
                ByWeekday = Weekdays.Monday,
            },
            participants: [new ParticipantInput(clara.Id)]);

        // Drei Wochen später ist sie belegt, ohne dass es dafür einen
        // Datensatz gäbe.
        Func<Task> clash = () =>
            EveningAsync([new ParticipantInput(clara.Id)], dayOffset: 21, hour: 20);

        await clash.Should().ThrowAsync<ScheduleConflictException>();
    }

    [Fact]
    public async Task Die_Abweichung_erbt_die_Besetzung_der_Serie()
    {
        Contact clara = await PersonAsync("Weber", "Clara");
        CatalogEntry ensemble = await FunctionAsync("ensemble", "Ensemble");

        EventSeries series = await Service.AddSeriesAsync(
            new EventSeries
            {
                Title = "Probe",
                StartDate = Monday,
                StartTime = new TimeOnly(19, 0),
                DurationMinutes = 120,
                TimeZoneId = "UTC",
                Frequency = RecurrenceFrequency.Weekly,
                ByWeekday = Weekdays.Monday,
            },
            participants: [new ParticipantInput(clara.Id, ensemble.Id)]);

        DateOnly third = Monday.AddDays(14);

        CalendarEvent moved = await Service.OverrideOccurrenceAsync(
            series.Id,
            At(third, 19, 0),
            item =>
            {
                item.StartsAt = At(third, 14, 0);
                item.EndsAt = At(third, 16, 0);
            });

        // Ohne das verschwände die Besetzung beim Verschieben, und niemand
        // wüsste mehr, wer zu der Probe kommen soll.
        EventParticipant inherited = moved.Participants.Should().ContainSingle().Subject;
        inherited.ContactId.Should().Be(clara.Id);
        inherited.FunctionId.Should().Be(ensemble.Id);
    }

    [Fact]
    public async Task Eine_Zusage_bleibt_beim_Aendern_der_Funktion_erhalten()
    {
        Contact clara = await PersonAsync("Weber", "Clara");
        CatalogEntry ensemble = await FunctionAsync("ensemble", "Ensemble");
        CatalogEntry stage = await FunctionAsync("technik", "Technik");

        CalendarEvent evening = await EveningAsync(
            [new ParticipantInput(clara.Id, ensemble.Id, ParticipationStatus.Accepted)]);

        Guid before = evening.Participants.Single().Id;

        await Service.UpdateEventAsync(
            evening.Id,
            _ => { },
            participants: [new ParticipantInput(clara.Id, stage.Id, ParticipationStatus.Accepted)]);

        EventParticipant after = await Context.EventParticipants.SingleAsync();

        // Derselbe Datensatz, nur eine andere Funktion -- sonst wäre die
        // Zusage mit der Änderung verloren.
        after.Id.Should().Be(before);
        after.FunctionId.Should().Be(stage.Id);
        after.Status.Should().Be(ParticipationStatus.Accepted);
    }

    [Fact]
    public async Task Wer_herausgenommen_wird_verschwindet()
    {
        Contact clara = await PersonAsync("Weber", "Clara");
        Contact ben = await PersonAsync("Schröder", "Ben");

        CalendarEvent evening = await EveningAsync(
            [new ParticipantInput(clara.Id), new ParticipantInput(ben.Id)]);

        await Service.UpdateEventAsync(
            evening.Id, _ => { }, participants: [new ParticipantInput(ben.Id)]);

        (await Context.EventParticipants.SingleAsync()).ContactId.Should().Be(ben.Id);
    }

    [Fact]
    public async Task Jemand_laesst_sich_nachtraeglich_dazunehmen()
    {
        Contact clara = await PersonAsync("Weber", "Clara");
        Contact ben = await PersonAsync("Schröder", "Ben");

        CalendarEvent evening = await EveningAsync([new ParticipantInput(clara.Id)]);

        await Service.UpdateEventAsync(
            evening.Id,
            _ => { },
            participants: [new ParticipantInput(clara.Id), new ParticipantInput(ben.Id)]);

        (await Context.EventParticipants.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Ohne_Angabe_bleibt_die_Besetzung_wie_sie_war()
    {
        Contact clara = await PersonAsync("Weber", "Clara");
        CalendarEvent evening = await EveningAsync([new ParticipantInput(clara.Id)]);

        // Wer nur den Titel ändert, will nicht die Besetzung abräumen.
        await Service.UpdateEventAsync(evening.Id, item => item.Title = "Anders");

        (await Context.EventParticipants.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Nach_Person_laesst_sich_filtern()
    {
        Contact clara = await PersonAsync("Weber", "Clara");
        Contact ben = await PersonAsync("Schröder", "Ben");

        await EveningAsync([new ParticipantInput(clara.Id)], hour: 14, title: "Mit Clara");
        await EveningAsync([new ParticipantInput(ben.Id)], hour: 20, title: "Mit Ben");

        List<CalendarOccurrence> hers = await Service.RangeAsync(
            Monday, Monday, people: [clara.Id]);

        hers.Should().ContainSingle().Which.Title.Should().Be("Mit Clara");
    }

    [Fact]
    public async Task Eine_Funktion_die_verwendet_wird_laesst_sich_nicht_loeschen()
    {
        Contact clara = await PersonAsync("Weber", "Clara");
        CatalogEntry ensemble = await FunctionAsync("ensemble", "Ensemble");

        await EveningAsync([new ParticipantInput(clara.Id, ensemble.Id)]);

        Func<Task> delete = () => new CatalogService(Context).DeleteAsync(ensemble.Id);

        await delete.Should().ThrowAsync<CatalogException>().WithMessage("*verwendet*");
    }

    [Fact]
    public async Task Mit_Nachdruck_laesst_sich_die_Doppelbesetzung_durchsetzen()
    {
        Contact clara = await PersonAsync("Weber", "Clara");

        await EveningAsync([new ParticipantInput(clara.Id)], hour: 19, hours: 3);
        await EveningAsync([new ParticipantInput(clara.Id)], hour: 20, force: true);

        (await Context.EventParticipants.CountAsync()).Should().Be(2);
    }
}
