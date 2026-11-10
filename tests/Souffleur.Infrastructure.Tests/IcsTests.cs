using System.Text;
using FluentAssertions;
using Souffleur.Domain.Calendar;
using Souffleur.Domain.Work;
using Souffleur.Infrastructure.Calendar;

namespace Souffleur.Infrastructure.Tests;

/// <summary>
/// Der Feed wird von fremder Software gelesen -- Apple Kalender, Outlook,
/// Google, Thunderbird. Was hier schiefgeht, sieht man nicht als Fehler,
/// sondern als fehlenden oder falschen Termin im Telefon.
/// </summary>
public class IcsTests
{
    private static readonly TimeZoneInfo Berlin =
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private static readonly DateTimeOffset Now =
        new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private static string Build(
        IReadOnlyList<CalendarEvent>? events = null,
        IReadOnlyList<EventSeries>? series = null,
        bool includeCancelled = true,
        IReadOnlyList<TaskItem>? deadlines = null) =>
        IcsWriter.Build(
            new IcsCalendar(
                "Spielplan", Berlin, events ?? [], series ?? [], includeCancelled,
                deadlines ?? []),
            Now);

    private static EventSeries Rehearsal() => new()
    {
        Title = "Probe Hamlet",
        Location = "Probebühne",
        StartDate = new DateOnly(2026, 11, 2),
        StartTime = new TimeOnly(19, 30),
        DurationMinutes = 180,
        TimeZoneId = "Europe/Berlin",
        Frequency = RecurrenceFrequency.Weekly,
        ByWeekday = Weekdays.Monday | Weekdays.Wednesday,
    };

    private static DateTimeOffset At(DateOnly date, int hour, int minute = 0) =>
        EventSeries.ToInstant(date, new TimeOnly(hour, minute), Berlin);

    // --- Grundgerüst ---

    [Fact]
    public void Jede_Zeile_endet_mit_CRLF()
    {
        string ics = Build([Premiere()]);

        // RFC 5545 kennt nur CRLF. Ein nacktes LF reicht manchen Anwendungen,
        // anderen nicht -- und die sagen nicht, warum.
        ics.Replace("\r\n", string.Empty).Should().NotContain("\n");
        ics.Should().StartWith("BEGIN:VCALENDAR\r\n").And.EndWith("END:VCALENDAR\r\n");
    }

    [Fact]
    public void Der_Kalender_nennt_seinen_Namen_und_wie_oft_zu_fragen_ist()
    {
        string ics = Build([Premiere()]);

        ics.Should().Contain("NAME:Spielplan");
        ics.Should().Contain("X-WR-CALNAME:Spielplan");
        ics.Should().Contain("REFRESH-INTERVAL;VALUE=DURATION:PT1H");
    }

    // --- Einzelne Termine ---

    [Fact]
    public void Ein_Einzeltermin_steht_als_fester_Zeitpunkt_in_UTC()
    {
        string ics = Build([Premiere()]);

        // 19:00 Berliner Winterzeit sind 18:00 UTC. Ein einmaliger Termin hat
        // einen festen Zeitpunkt -- dafür braucht es keine Zeitzone.
        ics.Should().Contain("DTSTART:20261120T180000Z");
        ics.Should().Contain("DTEND:20261120T210000Z");
        ics.Should().Contain("SUMMARY:Premiere Hamlet");
        ics.Should().Contain("STATUS:CONFIRMED");
    }

    [Fact]
    public void Ganztaegig_steht_als_Datum_und_endet_am_Folgetag()
    {
        var week = new CalendarEvent
        {
            Title = "Werkstattwoche",
            IsAllDay = true,
            StartsAt = At(new DateOnly(2026, 11, 2), 0),
            EndsAt = At(new DateOnly(2026, 11, 6), 23, 59),
        };

        string ics = Build([week]);

        ics.Should().Contain("DTSTART;VALUE=DATE:20261102");

        // DTEND ist ausschließend: Der letzte Tag ist der 6., also endet er
        // am 7. Ohne das fehlt in jeder Anwendung der letzte Tag.
        ics.Should().Contain("DTEND;VALUE=DATE:20261107");
    }

    [Fact]
    public void Vorgemerkt_heisst_TENTATIVE_und_abgesagt_CANCELLED()
    {
        CalendarEvent option = Premiere();
        option.Status = EventStatus.Planned;

        Build([option]).Should().Contain("STATUS:TENTATIVE");

        option.Status = EventStatus.Cancelled;
        Build([option]).Should().Contain("STATUS:CANCELLED");
    }

    [Fact]
    public void Ohne_abgesagte_Termine_fehlt_der_Termin_ganz()
    {
        CalendarEvent cancelled = Premiere();
        cancelled.Status = EventStatus.Cancelled;

        Build([cancelled], includeCancelled: false).Should().NotContain("BEGIN:VEVENT");
    }

    // --- Serien als Regel ---

    [Fact]
    public void Eine_Serie_geht_als_Regel_hinaus_nicht_als_einzelne_Termine()
    {
        string ics = Build(series: [Rehearsal()]);

        // Ein einziger Eintrag, und darin die Regel. Das ist der ganze Punkt:
        // Eine unbefristete Serie als Termine auszuschreiben ginge nicht.
        Occurrences(ics, "BEGIN:VEVENT").Should().Be(1);
        ics.Should().Contain("RRULE:FREQ=WEEKLY;BYDAY=MO,WE");
    }

    [Fact]
    public void Die_Serie_steht_in_Ortszeit_und_die_Zeitzone_steht_dabei()
    {
        string ics = Build(series: [Rehearsal()]);

        // In UTC wäre die Probe nach der Zeitumstellung eine Stunde verschoben.
        ics.Should().Contain("DTSTART;TZID=Europe/Berlin:20261102T193000");
        ics.Should().Contain("DTEND;TZID=Europe/Berlin:20261102T223000");

        // Ein TZID-Verweis ohne VTIMEZONE ist nach RFC 5545 ungültig.
        ics.Should().Contain("BEGIN:VTIMEZONE").And.Contain("TZID:Europe/Berlin");
        ics.Should().Contain("BEGIN:DAYLIGHT").And.Contain("BEGIN:STANDARD");
    }

    [Fact]
    public void Die_Zeitumstellung_steht_als_Regel_in_der_Zeitzone()
    {
        string ics = Build(series: [Rehearsal()]);

        // Letzter Sonntag im März und im Oktober -- so steht es in der
        // EU-Richtlinie, und so muss es im Feed stehen.
        ics.Should().Contain("FREQ=YEARLY;BYMONTH=3;BYDAY=-1SU");
        ics.Should().Contain("FREQ=YEARLY;BYMONTH=10;BYDAY=-1SU");
        ics.Should().Contain("TZOFFSETTO:+0200");
        ics.Should().Contain("TZOFFSETTO:+0100");
    }

    [Fact]
    public void Der_dritte_Freitag_im_Monat_wird_zu_BYDAY_3FR()
    {
        EventSeries board = Rehearsal();
        board.Frequency = RecurrenceFrequency.Monthly;
        board.ByWeekday = Weekdays.Friday;
        board.BySetPosition = 3;

        Build(series: [board]).Should().Contain("RRULE:FREQ=MONTHLY;BYDAY=3FR");
    }

    [Fact]
    public void Mehrere_Wochentage_mit_Position_brauchen_BYSETPOS()
    {
        EventSeries rule = Rehearsal();
        rule.Frequency = RecurrenceFrequency.Monthly;
        rule.ByWeekday = Weekdays.Saturday | Weekdays.Sunday;
        rule.BySetPosition = -1;

        // „Das letzte Wochenende im Monat" lässt sich nicht als -1SA,SU
        // schreiben -- das hieße etwas anderes.
        Build(series: [rule]).Should().Contain("RRULE:FREQ=MONTHLY;BYDAY=SA,SU;BYSETPOS=-1");
    }

    [Fact]
    public void Alle_zwei_Wochen_nennt_den_Wochenanfang()
    {
        EventSeries rule = Rehearsal();
        rule.Interval = 2;

        // Ohne WKST nähmen manche Anwendungen den Sonntag als Wochenanfang --
        // dann läge jeder zweite Termin eine Woche daneben.
        Build(series: [rule]).Should().Contain("INTERVAL=2").And.Contain("WKST=MO");
    }

    [Fact]
    public void Ein_Enddatum_wird_zu_UNTIL_in_UTC()
    {
        EventSeries rule = Rehearsal();
        rule.Until = new DateOnly(2026, 12, 31);

        // UNTIL meint einen Zeitpunkt, nicht einen Tag -- und muss bei einem
        // DTSTART mit TZID in UTC stehen. 19:30 Winterzeit sind 18:30 UTC.
        Build(series: [rule]).Should().Contain("UNTIL=20261231T183000Z");
    }

    [Fact]
    public void Eine_Anzahl_wird_zu_COUNT()
    {
        EventSeries rule = Rehearsal();
        rule.Count = 10;

        Build(series: [rule]).Should().Contain("COUNT=10").And.NotContain("UNTIL=");
    }

    // --- Abweichungen ---

    [Fact]
    public void Ein_verschobenes_Vorkommen_traegt_die_alte_Zeit_als_RECURRENCE_ID()
    {
        EventSeries rule = Rehearsal();
        DateOnly day = new(2026, 11, 9);

        rule.Exceptions.Add(new CalendarEvent
        {
            SeriesId = rule.Id,
            OccurrenceStart = At(day, 19, 30),
            Title = "Probe Hamlet",
            StartsAt = At(day, 14, 0),
            EndsAt = At(day, 17, 0),
        });

        string ics = Build(series: [rule]);

        // Die Kalenderanwendung findet das zu ersetzende Vorkommen über die
        // alte Zeit. Stünde hier die neue, erschiene der Termin zweimal.
        ics.Should().Contain("RECURRENCE-ID;TZID=Europe/Berlin:20261109T193000");
        ics.Should().Contain("DTSTART:20261109T130000Z");
        Occurrences(ics, "BEGIN:VEVENT").Should().Be(2);
    }

    [Fact]
    public void Ein_abgesagtes_Vorkommen_bleibt_sichtbar_wenn_es_mitgehen_soll()
    {
        string ics = Build(series: [WithCancelledOccurrence()], includeCancelled: true);

        ics.Should().Contain("RECURRENCE-ID;TZID=Europe/Berlin:20261104T193000");
        ics.Should().Contain("STATUS:CANCELLED");
        ics.Should().NotContain("EXDATE");
    }

    [Fact]
    public void Ein_abgesagtes_Vorkommen_verschwindet_sonst_als_EXDATE()
    {
        string ics = Build(series: [WithCancelledOccurrence()], includeCancelled: false);

        ics.Should().Contain("EXDATE;TZID=Europe/Berlin:20261104T193000");
        ics.Should().NotContain("STATUS:CANCELLED");
        Occurrences(ics, "BEGIN:VEVENT").Should().Be(1);
    }

    // --- Schreibweise ---

    [Fact]
    public void Semikolon_und_Komma_werden_geschuetzt()
    {
        CalendarEvent item = Premiere();
        item.Title = "Probe; mit Komma, und \\ Strich";

        string ics = Build([item]);

        ics.Should().Contain("SUMMARY:Probe\\; mit Komma\\, und \\\\ Strich");
    }

    [Fact]
    public void Ein_Zeilenumbruch_in_der_Notiz_wird_zu_einem_Zeichenpaar()
    {
        CalendarEvent item = Premiere();
        item.Description = "Erste Zeile\nZweite Zeile";

        Build([item]).Should().Contain("DESCRIPTION:Erste Zeile\\nZweite Zeile");
    }

    [Fact]
    public void Lange_Zeilen_werden_auf_75_Oktette_umgebrochen()
    {
        CalendarEvent item = Premiere();
        item.Title = new string('a', 200);

        string ics = Build([item]);

        foreach (string line in ics.Split("\r\n"))
        {
            Encoding.UTF8.GetByteCount(line).Should().BeLessThanOrEqualTo(75, line);
        }

        // Fortsetzungszeilen beginnen mit einem Leerzeichen -- daran erkennt
        // die Gegenseite, dass sie zusammengehören.
        ics.Should().Contain("\r\n a");
    }

    [Fact]
    public void Ein_Umlaut_wird_nicht_mitten_im_Zeichen_umgebrochen()
    {
        CalendarEvent item = Premiere();
        item.Title = new string('ü', 100);

        string ics = Build([item]);

        // Wäre mitten in ein Zeichen umgebrochen worden, käme beim Zurücklesen
        // etwas anderes heraus als das, was hineinging.
        string folded = string.Join(
            string.Empty,
            ics.Split("\r\n")
               .SkipWhile(line => !line.StartsWith("SUMMARY:", StringComparison.Ordinal))
               .TakeWhile(line => line.StartsWith("SUMMARY:", StringComparison.Ordinal)
                                  || line.StartsWith(' '))
               .Select(line => line.StartsWith(' ') ? line[1..] : line["SUMMARY:".Length..]));

        folded.Should().Be(item.Title);
    }

    [Fact]
    public void DTSTART_ist_der_erste_wirkliche_Termin_nicht_das_Startdatum()
    {
        EventSeries board = Rehearsal();
        board.Frequency = RecurrenceFrequency.Monthly;
        board.ByWeekday = Weekdays.Friday;
        board.BySetPosition = 3;

        // Die Serie ist am Montag, dem 2.11.2026 angelegt worden. Der dritte
        // Freitag dieses Monats ist der 20.11. -- der 2.11. ist kein Termin
        // der Regel.
        string ics = Build(series: [board]);

        // RFC 5545 zählt DTSTART immer als Vorkommen mit. Stünde hier der
        // 2.11., erschiene in jedem Telefon eine Sitzung, die es nicht gibt.
        ics.Should().Contain("DTSTART;TZID=Europe/Berlin:20261120T193000");
        ics.Should().NotContain("DTSTART;TZID=Europe/Berlin:20261102T193000");
    }

    [Fact]
    public void Eine_Regel_die_nie_zutrifft_steht_nicht_im_Feed()
    {
        EventSeries rule = Rehearsal();
        rule.ByWeekday = Weekdays.Sunday;

        // Beginn am Montag, Ende am Freitag derselben Woche -- dazwischen
        // liegt kein Sonntag.
        rule.Until = new DateOnly(2026, 11, 6);

        Build(series: [rule]).Should().NotContain("BEGIN:VEVENT");
    }

    [Fact]
    public void Eine_Frist_steht_als_ganztaegiger_Eintrag_mit_eigener_Kennung()
    {
        var deadline = new TaskItem
        {
            Title = "Verwendungsnachweis einreichen",
            DueDate = new DateOnly(2026, 11, 30),
        };

        string ics = Build(deadlines: [deadline]);

        ics.Should().Contain("SUMMARY:Frist: Verwendungsnachweis einreichen");
        ics.Should().Contain("DTSTART;VALUE=DATE:20261130");
        ics.Should().Contain("DTEND;VALUE=DATE:20261201");

        // Eigene Kennung: Die Frist gehört der Aufgabe, nicht dem Kalender.
        // Als Termin benannt stünde sie nach einer Verschiebung zweimal da.
        ics.Should().Contain($"UID:task-{deadline.Id:n}@souffleur");
    }

    private static CalendarEvent Premiere() => new()
    {
        Title = "Premiere Hamlet",
        StartsAt = new DateTimeOffset(2026, 11, 20, 19, 0, 0, TimeSpan.FromHours(1)),
        EndsAt = new DateTimeOffset(2026, 11, 20, 22, 0, 0, TimeSpan.FromHours(1)),
    };

    private static EventSeries WithCancelledOccurrence()
    {
        EventSeries rule = Rehearsal();
        DateOnly day = new(2026, 11, 4);

        rule.Exceptions.Add(new CalendarEvent
        {
            SeriesId = rule.Id,
            OccurrenceStart = At(day, 19, 30),
            Title = "Probe Hamlet",
            Status = EventStatus.Cancelled,
            StartsAt = At(day, 19, 30),
            EndsAt = At(day, 22, 30),
        });

        return rule;
    }

    private static int Occurrences(string text, string needle) =>
        text.Split(needle).Length - 1;
}
