namespace Souffleur.Domain.Calendar;

/// <summary>In welchem Takt sich eine Serie wiederholt.</summary>
public enum RecurrenceFrequency
{
    Daily = 1,
    Weekly = 2,
    Monthly = 3,
    Yearly = 4,
}

/// <summary>
/// Wochentage einer Serie. Als Flags, weil „Dienstag, Donnerstag und Samstag“
/// der Normalfall eines Spielplans ist und nicht der Sonderfall.
/// </summary>
[Flags]
public enum Weekdays
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,

    Weekdays_ = Monday | Tuesday | Wednesday | Thursday | Friday,
    Weekend = Saturday | Sunday,
    All = Weekdays_ | Weekend,
}

public static class WeekdayExtensions
{
    public static Weekdays ToFlag(this DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => Weekdays.Monday,
        DayOfWeek.Tuesday => Weekdays.Tuesday,
        DayOfWeek.Wednesday => Weekdays.Wednesday,
        DayOfWeek.Thursday => Weekdays.Thursday,
        DayOfWeek.Friday => Weekdays.Friday,
        DayOfWeek.Saturday => Weekdays.Saturday,
        _ => Weekdays.Sunday,
    };

    public static bool Includes(this Weekdays days, DayOfWeek day) =>
        (days & day.ToFlag()) != Weekdays.None;

    public static string Label(this Weekdays days)
    {
        if (days == Weekdays.None)
        {
            return "—";
        }

        string[] names = ["Mo", "Di", "Mi", "Do", "Fr", "Sa", "So"];
        Weekdays[] flags =
        [
            Weekdays.Monday, Weekdays.Tuesday, Weekdays.Wednesday, Weekdays.Thursday,
            Weekdays.Friday, Weekdays.Saturday, Weekdays.Sunday,
        ];

        return string.Join(", ", flags
            .Select((flag, index) => (flag, name: names[index]))
            .Where(pair => (days & pair.flag) != Weekdays.None)
            .Select(pair => pair.name));
    }
}
