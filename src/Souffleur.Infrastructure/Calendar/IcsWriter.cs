using System.Text;
using Souffleur.Domain.Calendar;
using Souffleur.Domain.Work;

namespace Souffleur.Infrastructure.Calendar;

/// <summary>
/// Was in einen Feed gehört. Die Serien tragen ihre Abweichungen mit.
/// </summary>
/// <param name="Deadlines">
/// Fristen aus den Aufgaben. Sie gehören nicht dem Kalender, erscheinen aber
/// darin (ADR-0006) -- und damit auch im Abonnement: Eine Frist, die im
/// Telefon fehlt, ist eine verpasste Frist.
/// </param>
public sealed record IcsCalendar(
    string Name,
    TimeZoneInfo Zone,
    IReadOnlyList<CalendarEvent> Events,
    IReadOnlyList<EventSeries> Series,
    bool IncludeCancelled,
    IReadOnlyList<TaskItem> Deadlines);

/// <summary>
/// Schreibt einen Kalender als iCalendar nach RFC 5545.
/// </summary>
/// <remarks>
/// Serien gehen als <c>RRULE</c> hinaus, nicht als ausgerechnete Termine.
/// Das ist der Grund, warum das Modell eine Regel speichert (ADR-0006): Ein
/// Feed, der zehn Jahre einer unbefristeten Probenserie ausrechnet, ist
/// unbrauchbar -- und einer, der bei zwei Jahren aufhört, lügt.
///
/// Die Uhrzeiten der Serien stehen in Ortszeit mit <c>TZID</c>, nicht in UTC.
/// Dieselbe Falle wie im Modell: Eine Probe, die „immer 19:30" ist, wäre als
/// UTC-Regel nach der Zeitumstellung um eine Stunde verschoben. Einzeltermine
/// dagegen haben einen festen Zeitpunkt und gehen als UTC hinaus -- dafür
/// braucht es keine Zeitzonendefinition.
/// </remarks>
public sealed class IcsWriter
{
    /// <summary>Wie oft eine Kalenderanwendung nachfragen soll.</summary>
    private const string RefreshInterval = "PT1H";

    private readonly StringBuilder builder = new();

    public static string Build(IcsCalendar calendar, DateTimeOffset now) =>
        new IcsWriter().Write(calendar, now);

    private string Write(IcsCalendar calendar, DateTimeOffset now)
    {
        Begin("VCALENDAR");
        Line("VERSION", "2.0");
        Line("PRODID", "-//Technologiestiftung Berlin//souffleur//DE");
        Line("CALSCALE", "GREGORIAN");
        Line("METHOD", "PUBLISH");

        // NAME und REFRESH-INTERVAL stehen in RFC 7986, die X-WR-Felder nicht --
        // aber ohne sie zeigen Outlook und Google den Namen nicht an.
        Line("NAME", Text(calendar.Name));
        Line("X-WR-CALNAME", Text(calendar.Name));
        Line("X-WR-TIMEZONE", calendar.Zone.Id);
        Line("REFRESH-INTERVAL;VALUE=DURATION", RefreshInterval);
        Line("X-PUBLISHED-TTL", RefreshInterval);

        bool needsZone = calendar.Series.Count > 0;
        if (needsZone)
        {
            WriteTimeZone(calendar.Zone);
        }

        foreach (CalendarEvent item in calendar.Events)
        {
            if (item.Status == EventStatus.Cancelled && !calendar.IncludeCancelled)
            {
                continue;
            }

            WriteEvent(item, calendar.Zone, now);
        }

        foreach (EventSeries series in calendar.Series)
        {
            WriteSeries(series, calendar, now);
        }

        foreach (TaskItem deadline in calendar.Deadlines)
        {
            WriteDeadline(deadline, now);
        }

        End("VCALENDAR");
        return builder.ToString();
    }

    // --- Einzelne Termine ---

    private void WriteEvent(CalendarEvent item, TimeZoneInfo zone, DateTimeOffset now)
    {
        Begin("VEVENT");
        Line("UID", Uid(item.SeriesId is null ? "event" : "series", item.SeriesId ?? item.Id));
        Line("DTSTAMP", Utc(now));

        if (item.OccurrenceStart is { } occurrence)
        {
            // Dieser Datensatz ersetzt ein Vorkommen der Serie. Der Zeitpunkt
            // hier ist der von der Regel errechnete, nicht der neue -- sonst
            // fände die Kalenderanwendung nicht, was sie ersetzen soll.
            Line($"RECURRENCE-ID;TZID={zone.Id}", Local(occurrence, zone));
        }

        WriteTimes(item.StartsAt, item.EndsAt, item.IsAllDay, zone);
        WriteDetails(item.Title, item.Description, item.Location, item.Status, item.ModifiedAt);
        End("VEVENT");
    }

    /// <summary>
    /// Eine Frist als ganztägiger Eintrag.
    /// </summary>
    /// <remarks>
    /// Eigene Kennung mit <c>task-</c>: Der Eintrag gehört der Aufgabe. Würde
    /// er wie ein Termin heißen, zeigte eine Kalenderanwendung ihn nach dem
    /// Verschieben der Aufgabe an zwei Stellen.
    /// </remarks>
    private void WriteDeadline(TaskItem task, DateTimeOffset now)
    {
        if (task.DueDate is not { } day)
        {
            return;
        }

        Begin("VEVENT");
        Line("UID", Uid("task", task.Id));
        Line("DTSTAMP", Utc(now));
        Line("DTSTART;VALUE=DATE", Date(day));
        Line("DTEND;VALUE=DATE", Date(day.AddDays(1)));
        Line("SUMMARY", Text($"Frist: {task.Title}"));

        if (!string.IsNullOrWhiteSpace(task.Description))
        {
            Line("DESCRIPTION", Text(task.Description));
        }

        Line("STATUS", "CONFIRMED");

        if (task.ModifiedAt is { } changed)
        {
            Line("LAST-MODIFIED", Utc(changed));
        }

        End("VEVENT");
    }

    // --- Serien ---

    private void WriteSeries(EventSeries series, IcsCalendar calendar, DateTimeOffset now)
    {
        TimeZoneInfo zone = series.Zone();

        // Abgesagte Vorkommen, die nicht mitgehen sollen, verschwinden als
        // EXDATE aus der Regel. Gehen sie mit, stehen sie als eigener Eintrag
        // mit STATUS:CANCELLED darunter -- dann sieht man, dass etwas entfällt,
        // statt nur eine Lücke zu finden.
        List<DateTimeOffset> excluded = [];
        List<CalendarEvent> changed = [];

        foreach (CalendarEvent exception in series.Exceptions)
        {
            if (exception.OccurrenceStart is not { } occurrence)
            {
                continue;
            }

            if (exception.Status == EventStatus.Cancelled && !calendar.IncludeCancelled)
            {
                excluded.Add(occurrence);
            }
            else
            {
                changed.Add(exception);
            }
        }

        // DTSTART muss der erste wirkliche Termin sein, nicht das gespeicherte
        // Startdatum. Eine Serie „dritter Freitag im Monat", die an einem
        // Dienstag beginnt, bekäme sonst in jeder Kalenderanwendung einen
        // Termin an diesem Dienstag -- RFC 5545 zählt DTSTART immer mit.
        if (FirstDay(series) is not { } first)
        {
            // Eine Regel, die nie zutrifft. Kommt vor, wenn das Enddatum vor
            // dem ersten passenden Tag liegt.
            return;
        }

        Begin("VEVENT");
        Line("UID", Uid("series", series.Id));
        Line("DTSTAMP", Utc(now));

        DateTimeOffset starts = EventSeries.ToInstant(first, series.StartTime, zone);
        DateTimeOffset ends = starts + TimeSpan.FromMinutes(Math.Max(1, series.DurationMinutes));

        if (series.IsAllDay)
        {
            Line("DTSTART;VALUE=DATE", Date(first));
            Line("DTEND;VALUE=DATE", Date(first.AddDays(1)));
        }
        else
        {
            Line($"DTSTART;TZID={zone.Id}", Local(starts, zone));
            Line($"DTEND;TZID={zone.Id}", Local(ends, zone));
        }

        Line("RRULE", Rule(series, zone));

        if (excluded.Count > 0)
        {
            // Alle in eine Zeile: So will es RFC 5545, und manche Anwendungen
            // lesen nur die erste.
            Line(
                series.IsAllDay ? "EXDATE;VALUE=DATE" : $"EXDATE;TZID={zone.Id}",
                string.Join(",", excluded.Order().Select(date =>
                    series.IsAllDay
                        ? Date(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(date, zone).DateTime))
                        : Local(date, zone))));
        }

        WriteDetails(
            series.Title, series.Description, series.Location, series.Status, series.ModifiedAt);
        End("VEVENT");

        foreach (CalendarEvent exception in changed)
        {
            WriteEvent(exception, zone, now);
        }
    }

    /// <summary>
    /// Der erste Tag, an dem die Regel zutrifft.
    /// </summary>
    /// <remarks>
    /// Gesucht wird mit derselben Regelauswertung, die auch das Raster füllt.
    /// So stimmen Feed und Oberfläche überein, statt sich in Randfällen
    /// auseinanderzuleben.
    /// </remarks>
    private static DateOnly? FirstDay(EventSeries series)
    {
        int interval = Math.Max(1, series.Interval);

        // Weit genug, um in jedem Rhythmus eine volle Periode zu überdecken.
        int span = series.Frequency switch
        {
            RecurrenceFrequency.Daily => interval + 1,
            RecurrenceFrequency.Weekly => (interval * 7) + 7,
            RecurrenceFrequency.Monthly => (interval * 31) + 31,
            _ => (interval * 366) + 366,
        };

        foreach (DateOnly day in series.LocalDates(
                     series.StartDate, series.StartDate.AddDays(span)))
        {
            return day;
        }

        return null;
    }

    /// <summary>Die Wiederholungsregel als RRULE.</summary>
    private static string Rule(EventSeries series, TimeZoneInfo zone)
    {
        int interval = Math.Max(1, series.Interval);
        List<string> parts = [$"FREQ={series.Frequency switch
        {
            RecurrenceFrequency.Daily => "DAILY",
            RecurrenceFrequency.Weekly => "WEEKLY",
            RecurrenceFrequency.Monthly => "MONTHLY",
            _ => "YEARLY",
        }}"];

        if (interval > 1)
        {
            parts.Add($"INTERVAL={interval}");
        }

        Weekdays days = series.ByWeekday == Weekdays.None
            ? series.StartDate.DayOfWeek.ToFlag()
            : series.ByWeekday;

        switch (series.Frequency)
        {
            case RecurrenceFrequency.Weekly:
                parts.Add($"BYDAY={Days(days)}");

                // Die Woche beginnt am Montag. Ohne WKST nähmen manche
                // Anwendungen den Sonntag -- bei „alle zwei Wochen" liegt
                // dann jeder zweite Termin eine Woche daneben.
                if (interval > 1)
                {
                    parts.Add("WKST=MO");
                }

                break;

            case RecurrenceFrequency.Monthly when series.BySetPosition is { } position:
                // Ein einzelner Wochentag lässt sich kurz schreiben (3FR),
                // mehrere brauchen BYSETPOS.
                if (Count(days) == 1)
                {
                    parts.Add($"BYDAY={position}{Days(days)}");
                }
                else
                {
                    parts.Add($"BYDAY={Days(days)}");
                    parts.Add($"BYSETPOS={position}");
                }

                break;

            case RecurrenceFrequency.Monthly:
                parts.Add($"BYMONTHDAY={series.ByMonthDay ?? series.StartDate.Day}");
                break;
        }

        if (series.Count is { } count)
        {
            parts.Add($"COUNT={count}");
        }
        else if (series.Until is { } until)
        {
            // UNTIL muss bei einem DTSTART mit TZID in UTC stehen -- und meint
            // den Zeitpunkt, nicht den Tag. Gemeint ist der letzte mögliche
            // Termin, also der Beginn an diesem Tag.
            parts.Add(series.IsAllDay
                ? $"UNTIL={Date(until)}"
                : $"UNTIL={Utc(EventSeries.ToInstant(until, series.StartTime, zone))}");
        }

        return string.Join(";", parts);
    }

    // --- Gemeinsames ---

    private void WriteTimes(
        DateTimeOffset starts, DateTimeOffset ends, bool allDay, TimeZoneInfo zone)
    {
        if (allDay)
        {
            DateOnly from = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(starts, zone).DateTime);
            DateOnly to = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(ends, zone).DateTime);

            // DTEND ist bei ganztägigen Terminen ausschließend: Ein Tag endet
            // am nächsten.
            Line("DTSTART;VALUE=DATE", Date(from));
            Line("DTEND;VALUE=DATE", Date(to.AddDays(1)));
            return;
        }

        // Ein einzelner Termin hat einen festen Zeitpunkt. Als UTC braucht er
        // keine Zeitzonendefinition und kann nirgends verrutschen.
        Line("DTSTART", Utc(starts));
        Line("DTEND", Utc(ends));
    }

    private void WriteDetails(
        string title,
        string? description,
        string? location,
        EventStatus status,
        DateTimeOffset? modified)
    {
        Line("SUMMARY", Text(title));

        if (!string.IsNullOrWhiteSpace(description))
        {
            Line("DESCRIPTION", Text(description));
        }

        if (!string.IsNullOrWhiteSpace(location))
        {
            Line("LOCATION", Text(location));
        }

        Line("STATUS", status switch
        {
            EventStatus.Planned => "TENTATIVE",
            EventStatus.Cancelled => "CANCELLED",
            _ => "CONFIRMED",
        });

        if (modified is { } changed)
        {
            // Damit eine Kalenderanwendung erkennt, dass sich etwas geändert
            // hat, statt den alten Stand zu behalten.
            Line("LAST-MODIFIED", Utc(changed));
        }
    }

    // --- Zeitzone ---

    /// <summary>
    /// Die Zeitzone als VTIMEZONE. Ohne sie ist ein TZID-Verweis nach RFC 5545
    /// ungültig, und strenge Anwendungen lehnen den ganzen Feed ab.
    /// </summary>
    private void WriteTimeZone(TimeZoneInfo zone)
    {
        Begin("VTIMEZONE");
        Line("TZID", zone.Id);

        TimeZoneInfo.AdjustmentRule? rule = CurrentRule(zone);

        if (rule is null)
        {
            Begin("STANDARD");
            Line("DTSTART", "19700101T000000");
            Line("TZOFFSETFROM", Offset(zone.BaseUtcOffset));
            Line("TZOFFSETTO", Offset(zone.BaseUtcOffset));
            End("STANDARD");
            End("VTIMEZONE");
            return;
        }

        TimeSpan standard = zone.BaseUtcOffset;
        TimeSpan daylight = standard + rule.DaylightDelta;

        WriteTransition("DAYLIGHT", rule.DaylightTransitionStart, standard, daylight);
        WriteTransition("STANDARD", rule.DaylightTransitionEnd, daylight, standard);
        End("VTIMEZONE");
    }

    /// <summary>
    /// Die heute geltende Umstellungsregel. Vergangene Regeln lässt der Feed
    /// weg: Sie beträfen nur Termine, die niemand mehr verschiebt.
    /// </summary>
    private static TimeZoneInfo.AdjustmentRule? CurrentRule(TimeZoneInfo zone)
    {
        if (!zone.SupportsDaylightSavingTime)
        {
            return null;
        }

        DateTime today = DateTime.UtcNow.Date;
        TimeZoneInfo.AdjustmentRule[] rules = zone.GetAdjustmentRules();

        return rules.FirstOrDefault(r => r.DateStart <= today && today <= r.DateEnd)
               ?? rules.LastOrDefault();
    }

    private void WriteTransition(
        string component, TimeZoneInfo.TransitionTime transition, TimeSpan from, TimeSpan to)
    {
        Begin(component);
        Line("DTSTART", TransitionStart(transition));
        Line("TZOFFSETFROM", Offset(from));
        Line("TZOFFSETTO", Offset(to));
        Line("RRULE", TransitionRule(transition));

        // TZNAME bleibt weg: Die Abkürzung käme aus der Spracheinstellung des
        // Servers, und die Anwendung zeigt ohnehin ihren eigenen Namen.
        End(component);
    }

    private static string TransitionStart(TimeZoneInfo.TransitionTime transition)
    {
        DateTime time = transition.TimeOfDay;

        if (transition.IsFixedDateRule)
        {
            return $"1970{transition.Month:00}{transition.Day:00}T{time:HHmmss}";
        }

        // Den wievielten Wochentag des Monats die Regel meint, auf 1970
        // abgebildet -- die Anwendung rechnet mit der RRULE weiter, aber
        // DTSTART soll zu ihr passen.
        var first = new DateTime(1970, transition.Month, 1);
        int offset = ((int)transition.DayOfWeek - (int)first.DayOfWeek + 7) % 7;
        DateTime day = first.AddDays(offset + ((transition.Week - 1) * 7));

        while (day.Month != transition.Month)
        {
            day = day.AddDays(-7);
        }

        return $"{day:yyyyMMdd}T{time:HHmmss}";
    }

    private static string TransitionRule(TimeZoneInfo.TransitionTime transition)
    {
        if (transition.IsFixedDateRule)
        {
            return $"FREQ=YEARLY;BYMONTH={transition.Month};BYMONTHDAY={transition.Day}";
        }

        // Woche 5 heißt „die letzte", und das schreibt RFC 5545 als -1.
        string week = transition.Week >= 5 ? "-1" : transition.Week.ToString();
        return $"FREQ=YEARLY;BYMONTH={transition.Month};BYDAY={week}{Day(transition.DayOfWeek)}";
    }

    // --- Formate ---

    private static string Offset(TimeSpan offset) =>
        $"{(offset < TimeSpan.Zero ? "-" : "+")}{Math.Abs(offset.Hours):00}{Math.Abs(offset.Minutes):00}";

    private static string Utc(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'");

    private static string Local(DateTimeOffset instant, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(instant, zone).DateTime.ToString("yyyyMMdd'T'HHmmss");

    private static string Date(DateOnly date) => date.ToString("yyyyMMdd");

    private static string Uid(string prefix, Guid id) => $"{prefix}-{id:n}@souffleur";

    private static string Days(Weekdays days) => string.Join(",", Enum
        .GetValues<DayOfWeek>()
        .OrderBy(day => ((int)day + 6) % 7)
        .Where(day => days.Includes(day))
        .Select(Day));

    private static int Count(Weekdays days) =>
        Enum.GetValues<DayOfWeek>().Count(day => days.Includes(day));

    private static string Day(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "MO",
        DayOfWeek.Tuesday => "TU",
        DayOfWeek.Wednesday => "WE",
        DayOfWeek.Thursday => "TH",
        DayOfWeek.Friday => "FR",
        DayOfWeek.Saturday => "SA",
        _ => "SU",
    };

    /// <summary>
    /// Sonderzeichen in Textwerten. Der Doppelpunkt gehört nicht dazu -- er
    /// trennt nur Name und Wert, im Wert selbst ist er erlaubt.
    /// </summary>
    private static string Text(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace(";", "\\;", StringComparison.Ordinal)
        .Replace(",", "\\,", StringComparison.Ordinal)
        .Replace("\r\n", "\\n", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\r", "\\n", StringComparison.Ordinal);

    private void Begin(string component) => Line("BEGIN", component);

    private void End(string component) => Line("END", component);

    private void Line(string name, string value) => Append($"{name}:{value}");

    /// <summary>
    /// Eine Zeile, auf 75 Oktette umgebrochen.
    /// </summary>
    /// <remarks>
    /// Gezählt wird in Bytes, nicht in Zeichen: Ein „ü" ist zwei Oktette, und
    /// wer in der Mitte eines Zeichens umbricht, erzeugt eine Datei, die
    /// manche Anwendung gar nicht mehr liest.
    /// </remarks>
    private void Append(string line)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(line);

        if (bytes.Length <= 75)
        {
            builder.Append(line).Append("\r\n");
            return;
        }

        int start = 0;
        bool first = true;

        while (start < bytes.Length)
        {
            // Fortsetzungszeilen beginnen mit einem Leerzeichen, das mitzählt.
            int take = Math.Min(first ? 75 : 74, bytes.Length - start);

            while (take > 0
                   && start + take < bytes.Length
                   && (bytes[start + take] & 0xC0) == 0x80)
            {
                take--;
            }

            if (take == 0)
            {
                break;
            }

            if (!first)
            {
                builder.Append(' ');
            }

            builder.Append(Encoding.UTF8.GetString(bytes, start, take)).Append("\r\n");
            start += take;
            first = false;
        }
    }
}
