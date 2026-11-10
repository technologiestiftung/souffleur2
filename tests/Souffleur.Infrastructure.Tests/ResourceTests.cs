using FluentAssertions;
using Souffleur.Domain.Calendar;
using Souffleur.Domain.Catalogs;
using Souffleur.Domain.Resources;
using Microsoft.EntityFrameworkCore;
using Souffleur.Infrastructure.Calendar;
using Souffleur.Infrastructure.Catalogs;
using Souffleur.Infrastructure.Resources;

namespace Souffleur.Infrastructure.Tests;

/// <summary>
/// Zwei Termine auf derselben Bühne sind kein Sonderfall, sondern der Fehler,
/// den eine Disposition am teuersten bezahlt. Diese Tests halten fest, wann
/// souffleur ihn meldet -- und wann nicht.
/// </summary>
public class ResourceTests : AccountingTestBase
{
    private CalendarService Calendar => new(Context);

    private ResourceService Resources => new(Context);

    /// <summary>
    /// Alles relativ zu heute: Die Belegungsprüfung schaut nach vorn, nicht in
    /// die Vergangenheit. Feste Daten im Testcode würden irgendwann hinter dem
    /// heutigen Tag liegen und die Tests lautlos sinnlos machen.
    /// </summary>
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

    private async Task<Resource> StageAsync(string name = "Großer Saal", bool exclusive = true)
    {
        var resource = new Resource { Name = name, IsExclusive = exclusive };
        Context.Resources.Add(resource);
        await Context.SaveChangesAsync();
        return resource;
    }

    private Task<CalendarEvent> EveningAsync(
        Resource resource,
        int dayOffset = 0,
        int hour = 20,
        int hours = 2,
        string title = "Vorstellung",
        EventStatus status = EventStatus.Confirmed,
        bool force = false)
    {
        DateOnly day = Monday.AddDays(dayOffset);

        return Calendar.AddEventAsync(
            new CalendarEvent
            {
                Title = title,
                Status = status,
                StartsAt = At(day, hour),

                // Über die Stunden rechnen und nicht über die Uhrzeit: 22 Uhr
                // plus zwei Stunden gibt es als Uhrzeit nicht.
                EndsAt = At(day, hour) + TimeSpan.FromHours(hours),
            },
            [resource.Id],
            force: force);
    }

    // --- Die Ressource selbst ---

    [Fact]
    public async Task Eine_Ressource_ohne_Belegung_laesst_sich_loeschen()
    {
        Resource stage = await StageAsync();

        await Resources.DeleteAsync(stage.Id);

        (await Resources.ListAsync(includeInactive: true)).Should().BeEmpty();
    }

    [Fact]
    public async Task Eine_belegte_Ressource_laesst_sich_nur_abschalten()
    {
        Resource stage = await StageAsync();
        await EveningAsync(stage);

        Func<Task> delete = () => Resources.DeleteAsync(stage.Id);

        await delete.Should().ThrowAsync<ResourceException>().WithMessage("*belegt*");

        // Abschalten geht -- und der Termin trägt sie weiter.
        await Resources.SetActiveAsync(stage.Id, false);
        (await Resources.ListAsync()).Should().BeEmpty();
        (await Resources.ListAsync(includeInactive: true)).Should().ContainSingle();
    }

    [Fact]
    public async Task Eine_abgeschaltete_Ressource_laesst_sich_nicht_mehr_waehlen()
    {
        Resource stage = await StageAsync();
        await Resources.SetActiveAsync(stage.Id, false);

        Func<Task> create = () => EveningAsync(stage);

        await create.Should().ThrowAsync<CalendarException>().WithMessage("*abgeschaltet*");
    }

    [Fact]
    public async Task Eine_Art_die_verwendet_wird_laesst_sich_nicht_loeschen()
    {
        var kind = new CatalogEntry
        {
            Catalog = CatalogKind.ResourceKind,
            Key = "buehne",
            Label = "Bühne",
        };
        Context.CatalogEntries.Add(kind);
        await Context.SaveChangesAsync();

        Context.Resources.Add(new Resource { Name = "Großer Saal", KindId = kind.Id });
        await Context.SaveChangesAsync();

        Func<Task> delete = () => new CatalogService(Context).DeleteAsync(kind.Id);

        await delete.Should().ThrowAsync<CatalogException>().WithMessage("*verwendet*");
    }

    // --- Die Belegungsprüfung ---

    [Fact]
    public async Task Zwei_Termine_auf_derselben_Buehne_werden_gemeldet()
    {
        Resource stage = await StageAsync();
        await EveningAsync(stage, hour: 20, hours: 3, title: "Hamlet");

        Func<Task> second = () => EveningAsync(stage, hour: 22, title: "Lesung");

        (await second.Should().ThrowAsync<ScheduleConflictException>())
            .Which.Conflicts.Should().ContainSingle()
            .Which.Title.Should().Be("Hamlet");
    }

    [Fact]
    public async Task Nacheinander_ist_keine_Ueberschneidung()
    {
        Resource stage = await StageAsync();
        await EveningAsync(stage, hour: 18, hours: 2, title: "Probe");

        // 18--20 und 20--22: Die Bühne ist um 20:00 frei.
        await EveningAsync(stage, hour: 20, hours: 2, title: "Vorstellung");

        (await Context.ResourceBookings.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Ein_abgesagter_Termin_gibt_die_Buehne_frei()
    {
        Resource stage = await StageAsync();
        await EveningAsync(stage, title: "Abgesagt", status: EventStatus.Cancelled);

        await EveningAsync(stage, title: "Stattdessen");

        (await Context.ResourceBookings.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Ein_vorgemerkter_Termin_blockiert_den_Saal()
    {
        // Eine Option auf einen Gastspieltermin ist keine halbe Belegung: Wer
        // sie übergeht, verkauft denselben Abend zweimal.
        Resource stage = await StageAsync();
        await EveningAsync(stage, title: "Option Gastspiel", status: EventStatus.Planned);

        Func<Task> second = () => EveningAsync(stage, title: "Eigene Vorstellung");

        await second.Should().ThrowAsync<ScheduleConflictException>();
    }

    [Fact]
    public async Task Eine_nicht_exklusive_Ressource_laesst_sich_teilen()
    {
        // Scheinwerfer gibt es mehrfach. Der Saal nicht.
        Resource lights = await StageAsync("Lichtpark", exclusive: false);

        await EveningAsync(lights, title: "Probe");
        await EveningAsync(lights, title: "Einrichten");

        (await Context.ResourceBookings.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Mit_Nachdruck_laesst_sich_die_Doppelbelegung_durchsetzen()
    {
        Resource stage = await StageAsync();
        await EveningAsync(stage, title: "Hamlet");

        // Die Disposition weiß manchmal, dass sie doppelt belegt.
        await EveningAsync(stage, title: "Trotzdem", force: true);

        (await Context.ResourceBookings.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Der_eigene_Termin_ist_kein_Konflikt()
    {
        Resource stage = await StageAsync();
        CalendarEvent evening = await EveningAsync(stage, title: "Hamlet");

        // Nur die Uhrzeit ändern: Der alte Stand desselben Termins darf nicht
        // im Weg stehen.
        await Calendar.UpdateEventAsync(
            evening.Id,
            item => item.StartsAt = item.StartsAt.AddMinutes(30),
            [stage.Id]);

        (await Context.ResourceBookings.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Eine_Serie_belegt_die_Buehne_an_jedem_ihrer_Termine()
    {
        Resource stage = await StageAsync();

        await Calendar.AddSeriesAsync(
            new EventSeries
            {
                Title = "Der Besuch der alten Dame",
                StartDate = Monday,
                StartTime = new TimeOnly(20, 0),
                DurationMinutes = 150,
                TimeZoneId = "UTC",
                Frequency = RecurrenceFrequency.Weekly,
                ByWeekday = Weekdays.Monday,
            },
            [stage.Id]);

        // Drei Wochen später ist derselbe Montag belegt -- obwohl es dafür
        // keinen Datensatz gibt.
        Func<Task> clash = () => EveningAsync(stage, dayOffset: 21, title: "Fremdveranstaltung");

        await clash.Should().ThrowAsync<ScheduleConflictException>();
    }

    [Fact]
    public async Task Ein_verschobenes_Vorkommen_gibt_seine_alte_Zeit_frei()
    {
        Resource stage = await StageAsync();

        EventSeries series = await Calendar.AddSeriesAsync(
            new EventSeries
            {
                Title = "Probe",
                StartDate = Monday,
                StartTime = new TimeOnly(20, 0),
                DurationMinutes = 120,
                TimeZoneId = "UTC",
                Frequency = RecurrenceFrequency.Weekly,
                ByWeekday = Weekdays.Monday,
            },
            [stage.Id]);

        DateOnly third = Monday.AddDays(14);

        // Die Probe wandert an diesem Montag auf den Nachmittag.
        CalendarEvent moved = await Calendar.OverrideOccurrenceAsync(
            series.Id,
            At(third, 20, 0),
            item =>
            {
                item.StartsAt = At(third, 14, 0);
                item.EndsAt = At(third, 16, 0);
            });

        // Die Belegung ist mitgegangen, nicht verloren.
        moved.Bookings.Should().ContainSingle().Which.ResourceId.Should().Be(stage.Id);

        // Und der Abend ist frei geworden.
        await EveningAsync(stage, dayOffset: 14, title: "Fremdveranstaltung");

        List<CalendarOccurrence> day = await Calendar.RangeAsync(third, third);
        day.Should().HaveCount(2);
    }

    [Fact]
    public async Task Eine_Ressource_laesst_sich_vom_Termin_wieder_nehmen()
    {
        Resource stage = await StageAsync();
        Resource room = await StageAsync("Probebühne 1");
        CalendarEvent evening = await EveningAsync(stage);

        await Calendar.UpdateEventAsync(evening.Id, _ => { }, [room.Id]);

        ResourceBooking booking = await Context.ResourceBookings.SingleAsync();
        booking.ResourceId.Should().Be(room.Id);
        booking.EventId.Should().Be(evening.Id);
    }

    [Fact]
    public async Task Der_Zeitraum_nennt_die_Ressourcen_des_Termins()
    {
        Resource stage = await StageAsync();
        await EveningAsync(stage, title: "Hamlet");

        List<CalendarOccurrence> found = await Calendar.RangeAsync(Monday, Monday);

        found.Should().ContainSingle()
             .Which.Resources.Should().ContainSingle()
             .Which.Name.Should().Be("Großer Saal");
    }

    [Fact]
    public async Task Nach_Ressource_laesst_sich_filtern()
    {
        Resource stage = await StageAsync();
        Resource room = await StageAsync("Probebühne 1");

        await EveningAsync(stage, hour: 20, title: "Vorstellung");
        await EveningAsync(room, hour: 14, title: "Probe");

        List<CalendarOccurrence> onStage = await Calendar.RangeAsync(
            Monday, Monday, resources: [stage.Id]);

        onStage.Should().ContainSingle().Which.Title.Should().Be("Vorstellung");
    }
}
