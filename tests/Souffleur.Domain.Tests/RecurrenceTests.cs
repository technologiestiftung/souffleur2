using FluentAssertions;
using Souffleur.Domain.Calendar;

namespace Souffleur.Domain.Tests;

/// <summary>
/// Die Serienregel ist die Stelle, an der ein Fehler am teuersten wäre: Ein um
/// eine Stunde verschobener Spielplan fällt niemandem auf, bis jemand zu spät
/// kommt. Deshalb steht hier auch, was die Zeitumstellung tut.
/// </summary>
public class RecurrenceTests
{
    private static readonly TimeZoneInfo Berlin =
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private static EventSeries Weekly(Weekdays days, DateOnly start, DateOnly? until = null) => new()
    {
        Title = "Probe",
        StartDate = start,
        StartTime = new TimeOnly(19, 30),
        Frequency = RecurrenceFrequency.Weekly,
        ByWeekday = days,
        Until = until,
    };

    [Fact]
    public void Woechentlich_an_mehreren_Tagen()
    {
        EventSeries series = Weekly(
            Weekdays.Tuesday | Weekdays.Thursday,
            new DateOnly(2026, 11, 2));

        List<DateOnly> dates =
            [.. series.LocalDates(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 15))];

        dates.Should().Equal(
            new DateOnly(2026, 11, 3),
            new DateOnly(2026, 11, 5),
            new DateOnly(2026, 11, 10),
            new DateOnly(2026, 11, 12));
    }

    [Fact]
    public void Vor_dem_ersten_Termin_gibt_es_nichts()
    {
        EventSeries series = Weekly(Weekdays.Monday, new DateOnly(2026, 11, 9));

        series.LocalDates(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 8))
              .Should().BeEmpty();
    }

    [Fact]
    public void Das_Enddatum_schneidet_ab()
    {
        EventSeries series = Weekly(
            Weekdays.Monday,
            new DateOnly(2026, 11, 2),
            until: new DateOnly(2026, 11, 16));

        series.LocalDates(new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 31))
              .Should().HaveCount(3);
    }

    [Fact]
    public void Die_Anzahl_zaehlt_ab_dem_ersten_Termin_nicht_ab_der_Abfrage()
    {
        // Drei Termine insgesamt: 2., 9. und 16. November. Wer ab dem 9. fragt,
        // darf nur noch zwei bekommen -- sonst liefe eine Serie mit fester
        // Anzahl je nach Ausschnitt unterschiedlich lange.
        EventSeries series = Weekly(Weekdays.Monday, new DateOnly(2026, 11, 2));
        series.Count = 3;

        series.LocalDates(new DateOnly(2026, 11, 9), new DateOnly(2027, 12, 31))
              .Should().Equal(new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 16));
    }

    [Fact]
    public void Jede_zweite_Woche()
    {
        EventSeries series = Weekly(Weekdays.Monday, new DateOnly(2026, 11, 2));
        series.Interval = 2;

        series.LocalDates(new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 15))
              .Should().Equal(
                  new DateOnly(2026, 11, 2),
                  new DateOnly(2026, 11, 16),
                  new DateOnly(2026, 11, 30),
                  new DateOnly(2026, 12, 14));
    }

    [Fact]
    public void Taeglich_mit_Abstand()
    {
        var series = new EventSeries
        {
            Title = "Aufbau",
            StartDate = new DateOnly(2026, 11, 2),
            StartTime = new TimeOnly(9, 0),
            Frequency = RecurrenceFrequency.Daily,
            Interval = 3,
        };

        series.LocalDates(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 12))
              .Should().Equal(
                  new DateOnly(2026, 11, 2),
                  new DateOnly(2026, 11, 5),
                  new DateOnly(2026, 11, 8),
                  new DateOnly(2026, 11, 11));
    }

    [Fact]
    public void Monatlich_am_einunddreissigsten_laesst_kurze_Monate_aus()
    {
        // So hält es RFC 5545: Was es nicht gibt, findet nicht statt. Ein
        // Jahrestag am 30. wäre schlechter als keiner.
        var series = new EventSeries
        {
            Title = "Monatsabschluss",
            StartDate = new DateOnly(2027, 1, 31),
            StartTime = new TimeOnly(10, 0),
            Frequency = RecurrenceFrequency.Monthly,
        };

        series.LocalDates(new DateOnly(2027, 1, 1), new DateOnly(2027, 6, 30))
              .Should().Equal(
                  new DateOnly(2027, 1, 31),
                  new DateOnly(2027, 3, 31),
                  new DateOnly(2027, 5, 31));
    }

    [Fact]
    public void Monatlich_am_dritten_Freitag()
    {
        var series = new EventSeries
        {
            Title = "Vorstandssitzung",
            StartDate = new DateOnly(2026, 11, 1),
            StartTime = new TimeOnly(18, 0),
            Frequency = RecurrenceFrequency.Monthly,
            ByWeekday = Weekdays.Friday,
            BySetPosition = 3,
        };

        series.LocalDates(new DateOnly(2026, 11, 1), new DateOnly(2027, 1, 31))
              .Should().Equal(
                  new DateOnly(2026, 11, 20),
                  new DateOnly(2026, 12, 18),
                  new DateOnly(2027, 1, 15));
    }

    [Fact]
    public void Monatlich_am_letzten_Freitag()
    {
        var series = new EventSeries
        {
            Title = "Hausversammlung",
            StartDate = new DateOnly(2026, 11, 1),
            StartTime = new TimeOnly(18, 0),
            Frequency = RecurrenceFrequency.Monthly,
            ByWeekday = Weekdays.Friday,
            BySetPosition = -1,
        };

        series.LocalDates(new DateOnly(2026, 11, 1), new DateOnly(2027, 1, 31))
              .Should().Equal(
                  new DateOnly(2026, 11, 27),
                  new DateOnly(2026, 12, 25),
                  new DateOnly(2027, 1, 29));
    }

    [Fact]
    public void Jaehrlich_am_neunundzwanzigsten_Februar_nur_im_Schaltjahr()
    {
        var series = new EventSeries
        {
            Title = "Jahrestag",
            StartDate = new DateOnly(2028, 2, 29),
            StartTime = new TimeOnly(12, 0),
            Frequency = RecurrenceFrequency.Yearly,
        };

        series.LocalDates(new DateOnly(2028, 1, 1), new DateOnly(2033, 12, 31))
              .Should().Equal(new DateOnly(2028, 2, 29), new DateOnly(2032, 2, 29));
    }

    [Fact]
    public void Eine_Abfrage_liefert_hoechstens_den_Deckel()
    {
        var series = new EventSeries
        {
            Title = "Täglich",
            StartDate = new DateOnly(2020, 1, 1),
            StartTime = new TimeOnly(8, 0),
            Frequency = RecurrenceFrequency.Daily,
        };

        series.LocalDates(new DateOnly(2020, 1, 1), new DateOnly(2030, 1, 1))
              .Should().HaveCount(EventSeries.MaxOccurrencesPerQuery);
    }

    // --- Zeitumstellung ---

    [Fact]
    public void Die_Uhrzeit_bleibt_ueber_die_Zeitumstellung_dieselbe()
    {
        // Der eigentliche Grund, warum eine Serie in Ortszeit rechnet: Die
        // Probe ist vorher wie nachher um 19:30, obwohl sich der Abstand zu
        // UTC ändert. Würde die Serie UTC fortschreiben, stünde ab Ende März
        // der halbe Spielplan eine Stunde falsch.
        EventSeries series = Weekly(Weekdays.Sunday, new DateOnly(2027, 3, 21));

        List<Occurrence> occurrences =
            [.. series.Occurrences(new DateOnly(2027, 3, 21), new DateOnly(2027, 4, 4))];

        occurrences.Should().HaveCount(3);
        occurrences.Should().AllSatisfy(o =>
            TimeZoneInfo.ConvertTime(o.StartsAt, Berlin).TimeOfDay
                .Should().Be(new TimeSpan(19, 30, 0)));

        // Vor der Umstellung +01:00, danach +02:00 -- in UTC also eine Stunde
        // früher, in Ortszeit unverändert.
        occurrences[0].StartsAt.Offset.Should().Be(TimeSpan.FromHours(1));
        occurrences[2].StartsAt.Offset.Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public void Die_Stunde_die_es_nicht_gibt_rueckt_vor()
    {
        // In der Nacht zum 28.03.2027 springt die Uhr von 02:00 auf 03:00.
        // 02:30 existiert nicht. Der Termin verschwindet nicht, sondern rückt.
        DateTimeOffset instant = EventSeries.ToInstant(
            new DateOnly(2027, 3, 28), new TimeOnly(2, 30), Berlin);

        TimeZoneInfo.ConvertTime(instant, Berlin).TimeOfDay
            .Should().Be(new TimeSpan(3, 30, 0));
    }

    [Fact]
    public void Die_Stunde_die_es_zweimal_gibt_wird_als_erste_gelesen()
    {
        // In der Nacht zum 31.10.2027 springt die Uhr von 03:00 auf 02:00.
        // 02:30 gibt es zweimal; es gilt die erste, also noch Sommerzeit.
        DateTimeOffset instant = EventSeries.ToInstant(
            new DateOnly(2027, 10, 31), new TimeOnly(2, 30), Berlin);

        instant.Offset.Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public void Eine_unbekannte_Zeitzone_macht_den_Kalender_nicht_leer()
    {
        EventSeries series = Weekly(Weekdays.Monday, new DateOnly(2026, 11, 2));
        series.TimeZoneId = "Mittelerde/Auenland";

        series.Zone().Should().Be(TimeZoneInfo.Utc);
        series.Occurrences(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30))
              .Should().NotBeEmpty();
    }

    // --- Beschreibung ---

    [Theory]
    [InlineData(RecurrenceFrequency.Daily, 1, "täglich")]
    [InlineData(RecurrenceFrequency.Daily, 3, "alle 3 Tage")]
    [InlineData(RecurrenceFrequency.Yearly, 1, "jährlich")]
    public void Die_Regel_laesst_sich_lesen(
        RecurrenceFrequency frequency, int interval, string expected)
    {
        var series = new EventSeries
        {
            Title = "X",
            StartDate = new DateOnly(2026, 11, 2),
            StartTime = new TimeOnly(19, 30),
            Frequency = frequency,
            Interval = interval,
        };

        series.Describe().Should().Be(expected);
    }

    [Fact]
    public void Die_Regel_nennt_Wochentage_und_Ende()
    {
        EventSeries series = Weekly(
            Weekdays.Tuesday | Weekdays.Thursday,
            new DateOnly(2026, 11, 2),
            until: new DateOnly(2027, 6, 30));

        series.Describe().Should().Be("wöchentlich Di, Do, bis 30.06.2027");
    }
}
