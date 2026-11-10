using Souffleur.Domain.Common;
using Souffleur.Domain.Resources;

namespace Souffleur.Domain.Calendar;

/// <summary>Ein ausgerechnetes Vorkommen einer Serie.</summary>
/// <param name="LocalDate">Der Tag in der Zeitzone der Serie -- womit das Raster rechnet.</param>
public sealed record Occurrence(DateOnly LocalDate, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

/// <summary>
/// Die Regel hinter einer Terminserie. Siehe ADR-0006.
/// </summary>
/// <remarks>
/// Gespeichert wird die Regel, nicht ihre Ergebnisse: „jeden Dienstag und
/// Donnerstag 19:30, bis 30.06." Die einzelnen Termine rechnet
/// <see cref="Occurrences"/> beim Abfragen aus.
///
/// Der Grund ist nicht Sparsamkeit, sondern dass Kopien zwei Fälle nicht
/// können: eine Serie ohne Ende, und eine nachträgliche Änderung, die genau
/// die Termine treffen soll, die noch niemand einzeln angefasst hat.
///
/// Abweichende einzelne Termine stehen als <see cref="CalendarEvent"/> mit
/// gesetzter SeriesId daneben und gewinnen gegen die Regel.
/// </remarks>
public class EventSeries : Entity
{
    // --- Vorlage für jeden Termin der Serie ---

    public required string Title { get; set; }

    public string? Description { get; set; }

    public string? Location { get; set; }

    public Guid? CategoryId { get; set; }

    /// <summary>Zu welcher Produktion die Serie gehoert -- die Probenwochen von Hamlet (ADR-0011).</summary>
    public Guid? ProductionId { get; set; }

    public EventStatus Status { get; set; } = EventStatus.Confirmed;

    public bool IsAllDay { get; set; }

    // --- Wann ---

    /// <summary>Der Tag des ersten Termins, in Ortszeit.</summary>
    public required DateOnly StartDate { get; set; }

    /// <summary>Die Uhrzeit, zu der jeder Termin beginnt -- in Ortszeit.</summary>
    public required TimeOnly StartTime { get; set; }

    public int DurationMinutes { get; set; } = 60;

    /// <summary>
    /// Die Zeitzone, in der die Uhrzeit gilt.
    /// </summary>
    /// <remarks>
    /// Hier liegt der Grund, warum eine Serie nicht in UTC rechnet: Eine Probe,
    /// die „immer 19:30“ ist, liegt im Winter auf 18:30 UTC und im Sommer auf
    /// 17:30 UTC. Wer den Zeitpunkt in UTC fortschreibt, verschiebt nach der
    /// Zeitumstellung den halben Spielplan um eine Stunde -- lautlos.
    /// </remarks>
    public string TimeZoneId { get; set; } = "Europe/Berlin";

    // --- Die Regel ---

    public RecurrenceFrequency Frequency { get; set; } = RecurrenceFrequency.Weekly;

    /// <summary>Jede wievielte Einheit: 2 bei „jede zweite Woche“.</summary>
    public int Interval { get; set; } = 1;

    /// <summary>
    /// Bei wöchentlich: an welchen Tagen. Bei monatlich zusammen mit
    /// <see cref="BySetPosition"/>: welcher Wochentag gemeint ist.
    /// </summary>
    public Weekdays ByWeekday { get; set; } = Weekdays.None;

    /// <summary>Bei monatlich: der Tag im Monat. Leer heißt: wie am Starttag.</summary>
    public int? ByMonthDay { get; set; }

    /// <summary>
    /// Bei monatlich: das wievielte Vorkommen des Wochentags im Monat.
    /// 1 bis 4, oder −1 für das letzte.
    /// </summary>
    public int? BySetPosition { get; set; }

    /// <summary>Letzter möglicher Tag. Leer heißt: unbefristet.</summary>
    public DateOnly? Until { get; set; }

    /// <summary>
    /// Wie viele Termine die Serie insgesamt hat. Zählt ab dem ersten, nicht ab
    /// dem abgefragten Zeitraum.
    /// </summary>
    public int? Count { get; set; }

    public List<CalendarEvent> Exceptions { get; set; } = [];

    /// <summary>
    /// Was die Serie belegt -- an jedem ihrer Termine. Eine Aussage über die
    /// Regel, nicht über Datensätze.
    /// </summary>
    public List<ResourceBooking> Bookings { get; set; } = [];

    /// <summary>Wer für die Serie eingeteilt ist. Ebenso eine Aussage über die Regel.</summary>
    public List<EventParticipant> Participants { get; set; } = [];

    /// <summary>
    /// Eine Serie ohne Ende würde beliebig viele Termine ergeben. Deshalb
    /// beantwortet der Server nur Zeiträume -- und auch die nur bis hierher.
    /// </summary>
    public const int MaxOccurrencesPerQuery = 750;

    private const int MaxPeriodsScanned = 10_000;

    public TimeZoneInfo Zone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        }
        catch (Exception exception) when (
            exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // Lieber UTC als gar kein Termin: Eine unbekannte Zeitzone ist ein
            // Konfigurationsfehler, kein Grund, den Kalender leer zu lassen.
            return TimeZoneInfo.Utc;
        }
    }

    /// <summary>
    /// Rechnet die Termine der Serie aus, die im Zeitraum liegen -- einschließlich
    /// beider Tage, in Ortszeit gemeint.
    /// </summary>
    public IEnumerable<Occurrence> Occurrences(DateOnly from, DateOnly to)
    {
        TimeZoneInfo zone = Zone();
        var duration = TimeSpan.FromMinutes(Math.Max(1, DurationMinutes));

        foreach (DateOnly date in LocalDates(from, to))
        {
            DateTimeOffset starts = ToInstant(date, StartTime, zone);
            yield return new Occurrence(date, starts, starts + duration);
        }
    }

    /// <summary>
    /// Die Tage der Serie im Zeitraum. Getrennt von der Umrechnung, weil das
    /// Jahresraster mit Tagen arbeitet und nicht mit Zeitpunkten.
    /// </summary>
    public IEnumerable<DateOnly> LocalDates(DateOnly from, DateOnly to)
    {
        if (to < StartDate)
        {
            yield break;
        }

        DateOnly last = Until is { } until && until < to ? until : to;
        int interval = Math.Max(1, Interval);
        int emitted = 0;
        int counted = 0;

        for (int period = FirstPeriod(from, interval), scanned = 0;
             scanned < MaxPeriodsScanned;
             period++, scanned++)
        {
            bool beyond = true;

            foreach (DateOnly candidate in PeriodDates(period, interval))
            {
                if (candidate < StartDate)
                {
                    beyond = false;
                    continue;
                }

                if (candidate > last)
                {
                    continue;
                }

                beyond = false;
                counted++;

                if (Count is { } limit && counted > limit)
                {
                    yield break;
                }

                if (candidate >= from)
                {
                    yield return candidate;

                    if (++emitted >= MaxOccurrencesPerQuery)
                    {
                        yield break;
                    }
                }
            }

            // Erst abbrechen, wenn die ganze Periode hinter dem Zeitraum liegt:
            // Bei „jeden Montag und Freitag“ liegt der Montag noch drin, wenn
            // der Freitag schon draußen ist.
            if (beyond && PeriodStart(period, interval) > last)
            {
                yield break;
            }
        }
    }

    /// <summary>
    /// Wo die Iteration anfangen darf. Ohne <see cref="Count"/> lässt sich zum
    /// Zeitraum springen; mit Count muss von vorn gezählt werden, sonst stimmt
    /// „zehnmal“ nicht mehr.
    /// </summary>
    private int FirstPeriod(DateOnly from, int interval)
    {
        if (Count is not null || from <= StartDate)
        {
            return 0;
        }

        int periods = Frequency switch
        {
            RecurrenceFrequency.Daily => (from.DayNumber - StartDate.DayNumber) / interval,
            RecurrenceFrequency.Weekly =>
                (WeekStart(from).DayNumber - WeekStart(StartDate).DayNumber) / 7 / interval,
            RecurrenceFrequency.Monthly => MonthsBetween(StartDate, from) / interval,
            RecurrenceFrequency.Yearly => (from.Year - StartDate.Year) / interval,
            _ => 0,
        };

        // Eine Periode Sicherheitsabstand: Der Zeitraum kann mitten in einer
        // Periode beginnen, deren frühere Tage noch hineinragen.
        return Math.Max(0, periods - 1);
    }

    private DateOnly PeriodStart(int period, int interval) => Frequency switch
    {
        RecurrenceFrequency.Daily => StartDate.AddDays(period * interval),
        RecurrenceFrequency.Weekly => WeekStart(StartDate).AddDays(period * interval * 7),
        RecurrenceFrequency.Monthly => FirstOfMonth(StartDate).AddMonths(period * interval),
        RecurrenceFrequency.Yearly => FirstOfMonth(StartDate).AddYears(period * interval),
        _ => StartDate,
    };

    private IEnumerable<DateOnly> PeriodDates(int period, int interval)
    {
        switch (Frequency)
        {
            case RecurrenceFrequency.Daily:
                yield return StartDate.AddDays(period * interval);
                break;

            case RecurrenceFrequency.Weekly:
            {
                Weekdays days = ByWeekday == Weekdays.None
                    ? StartDate.DayOfWeek.ToFlag()
                    : ByWeekday;

                DateOnly monday = WeekStart(StartDate).AddDays(period * interval * 7);
                for (int offset = 0; offset < 7; offset++)
                {
                    DateOnly day = monday.AddDays(offset);
                    if (days.Includes(day.DayOfWeek))
                    {
                        yield return day;
                    }
                }

                break;
            }

            case RecurrenceFrequency.Monthly:
            {
                DateOnly month = FirstOfMonth(StartDate).AddMonths(period * interval);
                DateOnly? day = BySetPosition is { } position
                    ? NthWeekdayOfMonth(month, ByWeekday == Weekdays.None
                        ? StartDate.DayOfWeek.ToFlag()
                        : ByWeekday, position)
                    : DayOfMonthOrNull(month, ByMonthDay ?? StartDate.Day);

                if (day is { } found)
                {
                    yield return found;
                }

                break;
            }

            case RecurrenceFrequency.Yearly:
            {
                DateOnly month = FirstOfMonth(StartDate).AddYears(period * interval);

                // Den 29.02. gibt es nicht jedes Jahr. Überspringen statt auf
                // den 28. rücken -- so hält es RFC 5545, und ein Jahrestag am
                // falschen Tag ist schlechter als keiner.
                if (DayOfMonthOrNull(month, StartDate.Day) is { } found)
                {
                    yield return found;
                }

                break;
            }
        }
    }

    private static DateOnly WeekStart(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    private static int MonthsBetween(DateOnly from, DateOnly to) =>
        ((to.Year - from.Year) * 12) + to.Month - from.Month;

    private static DateOnly? DayOfMonthOrNull(DateOnly month, int day) =>
        day >= 1 && day <= DateTime.DaysInMonth(month.Year, month.Month)
            ? new DateOnly(month.Year, month.Month, day)
            : null;

    /// <summary>
    /// Das wievielte Vorkommen der gewünschten Wochentage im Monat; −1 ist das
    /// letzte. So lässt sich „jeder letzte Freitag im Monat“ sagen.
    /// </summary>
    private static DateOnly? NthWeekdayOfMonth(DateOnly month, Weekdays days, int position)
    {
        List<DateOnly> matching = [];
        for (int day = 1; day <= DateTime.DaysInMonth(month.Year, month.Month); day++)
        {
            var candidate = new DateOnly(month.Year, month.Month, day);
            if (days.Includes(candidate.DayOfWeek))
            {
                matching.Add(candidate);
            }
        }

        int index = position > 0 ? position - 1 : matching.Count + position;
        return index >= 0 && index < matching.Count ? matching[index] : null;
    }

    /// <summary>
    /// Ortszeit in einen Zeitpunkt umrechnen -- mit beiden Fällen der
    /// Zeitumstellung.
    /// </summary>
    public static DateTimeOffset ToInstant(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        DateTime local = date.ToDateTime(time, DateTimeKind.Unspecified);

        // Frühjahr: Die Stunde gibt es nicht. Der Termin rückt vor, statt zu
        // verschwinden -- in der Nacht der Umstellung ist das die Auslegung,
        // die niemanden einen Termin übersehen lässt.
        for (int attempt = 0; attempt < 4 && zone.IsInvalidTime(local); attempt++)
        {
            local = local.AddHours(1);
        }

        if (zone.IsAmbiguousTime(local))
        {
            // Herbst: Die Stunde gibt es zweimal. Es gilt die erste, also die
            // mit dem größeren Abstand zu UTC (Sommerzeit).
            TimeSpan offset = zone.GetAmbiguousTimeOffsets(local).Max();
            return new DateTimeOffset(local, offset);
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    /// <summary>Die Regel in Worten -- für Listen und das Terminfenster.</summary>
    public string Describe()
    {
        int interval = Math.Max(1, Interval);
        string rhythm = Frequency switch
        {
            RecurrenceFrequency.Daily => interval == 1 ? "täglich" : $"alle {interval} Tage",
            RecurrenceFrequency.Weekly => (interval == 1 ? "wöchentlich" : $"alle {interval} Wochen")
                + $" {(ByWeekday == Weekdays.None ? StartDate.DayOfWeek.ToFlag() : ByWeekday).Label()}",
            RecurrenceFrequency.Monthly => (interval == 1 ? "monatlich" : $"alle {interval} Monate")
                + (BySetPosition is { } position
                    ? $", {Position(position)} {(ByWeekday == Weekdays.None ? StartDate.DayOfWeek.ToFlag() : ByWeekday).Label()}"
                    : $", am {ByMonthDay ?? StartDate.Day}."),
            RecurrenceFrequency.Yearly => interval == 1 ? "jährlich" : $"alle {interval} Jahre",
            _ => string.Empty,
        };

        string ending = (Until, Count) switch
        {
            ({ } until, _) => $", bis {until:dd.MM.yyyy}",
            (_, { } count) => $", {count}-mal",
            _ => string.Empty,
        };

        return $"{rhythm}{ending}";
    }

    private static string Position(int position) => position switch
    {
        1 => "erster",
        2 => "zweiter",
        3 => "dritter",
        4 => "vierter",
        -1 => "letzter",
        _ => $"{position}.",
    };
}
