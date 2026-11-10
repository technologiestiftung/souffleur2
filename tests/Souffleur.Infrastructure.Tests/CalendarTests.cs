using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Calendar;
using Souffleur.Domain.Catalogs;
using Souffleur.Infrastructure.Calendar;

namespace Souffleur.Infrastructure.Tests;

/// <summary>
/// Der Kalender rechnet Serien aus und legt die Abweichungen darüber. Diese
/// Tests halten fest, dass dabei nichts doppelt und nichts verloren geht.
/// </summary>
public class CalendarTests : AccountingTestBase
{
    private CalendarService Service => new(Context);

    private async Task<EventSeries> WeeklyRehearsalAsync()
    {
        var series = new EventSeries
        {
            Title = "Probe Hamlet",
            Location = "Probebühne",
            StartDate = new DateOnly(2026, 11, 2),
            StartTime = new TimeOnly(19, 30),
            DurationMinutes = 180,
            Frequency = RecurrenceFrequency.Weekly,
            ByWeekday = Weekdays.Monday,
        };

        return await Service.AddSeriesAsync(series);
    }

    [Fact]
    public async Task Eine_Serie_erscheint_im_Zeitraum_ohne_eigene_Datensaetze()
    {
        await WeeklyRehearsalAsync();

        List<CalendarOccurrence> found = await Service.RangeAsync(
            new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));

        found.Should().HaveCount(5);
        found.Should().AllSatisfy(o => o.IsFromSeries.Should().BeTrue());
        found.Should().AllSatisfy(o => o.EventId.Should().BeNull());

        // Nichts davon liegt als Zeile in der Datenbank -- das ist der Punkt.
        (await Context.CalendarEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Ein_verschobenes_Vorkommen_steht_nur_am_neuen_Platz()
    {
        EventSeries series = await WeeklyRehearsalAsync();
        TimeZoneInfo zone = await Service.ZoneAsync();

        DateTimeOffset second = EventSeries.ToInstant(
            new DateOnly(2026, 11, 9), new TimeOnly(19, 30), zone);

        await Service.OverrideOccurrenceAsync(series.Id, second, item =>
        {
            item.StartsAt = second.AddDays(1);
            item.EndsAt = second.AddDays(1).AddHours(3);
        });

        List<CalendarOccurrence> found = await Service.RangeAsync(
            new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));

        found.Should().HaveCount(5);
        found.Select(o => o.LocalDate).Should().Equal(
            new DateOnly(2026, 11, 2),
            new DateOnly(2026, 11, 10),
            new DateOnly(2026, 11, 16),
            new DateOnly(2026, 11, 23),
            new DateOnly(2026, 11, 30));
    }

    [Fact]
    public async Task Ein_abgesagtes_Vorkommen_bleibt_sichtbar()
    {
        // Abgesagt heißt nicht verschwunden: Sonst weiß am Montag niemand
        // mehr, warum der Mittwoch frei ist.
        EventSeries series = await WeeklyRehearsalAsync();
        TimeZoneInfo zone = await Service.ZoneAsync();

        DateTimeOffset second = EventSeries.ToInstant(
            new DateOnly(2026, 11, 9), new TimeOnly(19, 30), zone);
        await Service.CancelOccurrenceAsync(series.Id, second);

        List<CalendarOccurrence> all = await Service.RangeAsync(
            new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));
        all.Should().HaveCount(5);
        all.Single(o => o.LocalDate == new DateOnly(2026, 11, 9))
           .Status.Should().Be(EventStatus.Cancelled);

        List<CalendarOccurrence> without = await Service.RangeAsync(
            new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), includeCancelled: false);
        without.Should().HaveCount(4);
    }

    [Fact]
    public async Task Eine_zurueckgenommene_Abweichung_folgt_wieder_der_Regel()
    {
        EventSeries series = await WeeklyRehearsalAsync();
        TimeZoneInfo zone = await Service.ZoneAsync();

        DateTimeOffset second = EventSeries.ToInstant(
            new DateOnly(2026, 11, 9), new TimeOnly(19, 30), zone);

        await Service.CancelOccurrenceAsync(series.Id, second);
        await Service.ResetOccurrenceAsync(series.Id, second);

        List<CalendarOccurrence> found = await Service.RangeAsync(
            new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));

        found.Should().HaveCount(5);
        found.Should().AllSatisfy(o => o.Status.Should().Be(EventStatus.Confirmed));
        (await Context.CalendarEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Eine_Regelaenderung_laesst_die_Abweichung_stehen()
    {
        // Eine einzeln verschobene Probe ist eine Entscheidung. Wer die Regel
        // ändert, soll sie nicht ungefragt kassieren.
        EventSeries series = await WeeklyRehearsalAsync();
        TimeZoneInfo zone = await Service.ZoneAsync();

        DateTimeOffset second = EventSeries.ToInstant(
            new DateOnly(2026, 11, 9), new TimeOnly(19, 30), zone);
        await Service.CancelOccurrenceAsync(series.Id, second);

        await Service.UpdateSeriesAsync(series.Id, s => s.Title = "Probe Hamlet, neu");

        (await Context.CalendarEvents.CountAsync()).Should().Be(1);

        await Service.UpdateSeriesAsync(
            series.Id, s => s.Title = "Probe Hamlet", keepExceptions: false);

        (await Context.CalendarEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Ein_Einzeltermin_steht_neben_der_Serie()
    {
        await WeeklyRehearsalAsync();

        await Service.AddEventAsync(new CalendarEvent
        {
            Title = "Premiere",
            StartsAt = new DateTimeOffset(2026, 11, 20, 18, 0, 0, TimeSpan.FromHours(1)),
            EndsAt = new DateTimeOffset(2026, 11, 20, 21, 0, 0, TimeSpan.FromHours(1)),
        });

        List<CalendarOccurrence> found = await Service.RangeAsync(
            new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 20));

        found.Should().ContainSingle();
        found[0].Title.Should().Be("Premiere");
        found[0].IsFromSeries.Should().BeFalse();
    }

    [Fact]
    public async Task Ein_Termin_am_Rand_des_Zeitraums_faellt_nicht_heraus()
    {
        // Der späte Abend des letzten Tages: In UTC liegt er schon im Folgetag.
        // Wer das Fenster nicht in Ortszeit bildet, verliert ihn.
        await Service.AddEventAsync(new CalendarEvent
        {
            Title = "Spätvorstellung",
            StartsAt = new DateTimeOffset(2026, 11, 30, 23, 0, 0, TimeSpan.FromHours(1)),
            EndsAt = new DateTimeOffset(2026, 12, 1, 0, 30, 0, TimeSpan.FromHours(1)),
        });

        List<CalendarOccurrence> found = await Service.RangeAsync(
            new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));

        found.Should().ContainSingle();
        found[0].LocalDate.Should().Be(new DateOnly(2026, 11, 30));
    }

    [Fact]
    public async Task Ein_zu_langer_Zeitraum_wird_abgelehnt()
    {
        Func<Task> query = () => Service.RangeAsync(
            new DateOnly(2026, 1, 1), new DateOnly(2030, 1, 1));

        await query.Should().ThrowAsync<CalendarException>()
                   .WithMessage("*Zeitraum*");
    }

    [Fact]
    public async Task Ein_Termin_endet_nicht_vor_seinem_Anfang()
    {
        Func<Task> create = () => Service.AddEventAsync(new CalendarEvent
        {
            Title = "Verdreht",
            StartsAt = new DateTimeOffset(2026, 11, 20, 20, 0, 0, TimeSpan.Zero),
            EndsAt = new DateTimeOffset(2026, 11, 20, 18, 0, 0, TimeSpan.Zero),
        });

        await create.Should().ThrowAsync<CalendarException>();
    }

    [Fact]
    public async Task Eine_abgeschaltete_Kategorie_laesst_sich_nicht_mehr_waehlen()
    {
        var category = new CatalogEntry
        {
            Catalog = CatalogKind.EventCategory,
            Key = "probe",
            Label = "Probe",
            IsActive = false,
        };
        Context.CatalogEntries.Add(category);
        await Context.SaveChangesAsync();

        Func<Task> create = () => Service.AddEventAsync(new CalendarEvent
        {
            Title = "Probe",
            CategoryId = category.Id,
            StartsAt = new DateTimeOffset(2026, 11, 20, 18, 0, 0, TimeSpan.Zero),
            EndsAt = new DateTimeOffset(2026, 11, 20, 21, 0, 0, TimeSpan.Zero),
        });

        await create.Should().ThrowAsync<CalendarException>()
                    .WithMessage("*abgeschaltet*");
    }

    [Fact]
    public async Task Eine_Serie_braucht_entweder_ein_Ende_oder_eine_Anzahl()
    {
        Func<Task> create = () => Service.AddSeriesAsync(new EventSeries
        {
            Title = "Beides",
            StartDate = new DateOnly(2026, 11, 2),
            StartTime = new TimeOnly(19, 30),
            Until = new DateOnly(2027, 6, 30),
            Count = 10,
        });

        await create.Should().ThrowAsync<CalendarException>()
                    .WithMessage("*nicht beides*");
    }
}
