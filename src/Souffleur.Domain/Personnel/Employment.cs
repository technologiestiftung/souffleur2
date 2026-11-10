using Souffleur.Domain.Common;
using Souffleur.Domain.Contacts;

namespace Souffleur.Domain.Personnel;

/// <summary>Die Wochentage, an denen jemand arbeitet.</summary>
/// <remarks>
/// Als Tage und nicht als Anzahl: Am Theater arbeitet die Abendspielleitung
/// Mittwoch bis Sonntag, und ein Urlaub von Montag bis Freitag kostet sie drei
/// Tage, nicht fuenf.
/// </remarks>
[Flags]
public enum WorkWeekdays
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,

    MondayToFriday = Monday | Tuesday | Wednesday | Thursday | Friday,
}

public static class WorkWeekdayExtensions
{
    public static WorkWeekdays Of(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => WorkWeekdays.Monday,
        DayOfWeek.Tuesday => WorkWeekdays.Tuesday,
        DayOfWeek.Wednesday => WorkWeekdays.Wednesday,
        DayOfWeek.Thursday => WorkWeekdays.Thursday,
        DayOfWeek.Friday => WorkWeekdays.Friday,
        DayOfWeek.Saturday => WorkWeekdays.Saturday,
        _ => WorkWeekdays.Sunday,
    };

    public static bool Includes(this WorkWeekdays days, DayOfWeek day) => (days & Of(day)) != 0;

    public static int Count(this WorkWeekdays days) =>
        System.Numerics.BitOperations.PopCount((uint)days & 127);
}

/// <summary>
/// Was im Arbeitsvertrag steht, soweit souffleur es zum Rechnen braucht:
/// Stunden, Arbeitstage, Urlaubsanspruch.
/// </summary>
/// <remarks>
/// Kein Gehalt, keine Steuerklasse, keine Sozialversicherungsnummer. Die
/// gehoeren in die Lohnabrechnung, und die macht das Steuerbuero (ADR-0002).
///
/// Ein Verlauf statt eines Feldes am Kontakt: Wer in Elternzeit auf zwanzig
/// Stunden geht, hat im Januar ein anderes Soll als im Juni, und das
/// Stundenkonto des Januars soll sich dadurch nicht nachtraeglich aendern.
/// Ein Eintrag gilt ab <see cref="ValidFrom"/> bis zum naechsten -- oder bis
/// <see cref="EndsOn"/>, wenn jemand geht.
/// </remarks>
public class Employment : Entity
{
    public required Guid ContactId { get; set; }

    public Contact? Contact { get; set; }

    public required DateOnly ValidFrom { get; set; }

    /// <summary>Der letzte Arbeitstag, wenn das Arbeitsverhaeltnis endet.</summary>
    public DateOnly? EndsOn { get; set; }

    /// <summary>Vertragliche Wochenstunden.</summary>
    public decimal WeeklyHours { get; set; }

    public WorkWeekdays WorkDays { get; set; } = WorkWeekdays.MondayToFriday;

    /// <summary>
    /// Urlaubstage im Jahr, gezaehlt in den eigenen Arbeitstagen. Wer drei Tage
    /// die Woche arbeitet, hat hier die drei Fuenftel eines vollen Anspruchs --
    /// das rechnet das Haus, nicht souffleur, weil Vertraege es unterschiedlich
    /// regeln.
    /// </summary>
    public decimal AnnualLeaveDays { get; set; }

    public string? Note { get; set; }

    /// <summary>Das Soll eines Arbeitstags in Minuten.</summary>
    public int DailyTargetMinutes =>
        WorkDays.Count() == 0 ? 0 : (int)Math.Round(WeeklyHours * 60m / WorkDays.Count());

    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];

        if (WeeklyHours is < 0 or > 60)
        {
            problems.Add("Die Wochenstunden liegen zwischen 0 und 60.");
        }

        if (WorkDays == WorkWeekdays.None)
        {
            problems.Add("Es fehlt, an welchen Tagen gearbeitet wird.");
        }

        if (AnnualLeaveDays is < 0 or > 60)
        {
            problems.Add("Der Urlaubsanspruch liegt zwischen 0 und 60 Tagen.");
        }

        if (EndsOn is { } end && end < ValidFrom)
        {
            problems.Add("Das Ende liegt vor dem Beginn.");
        }

        return problems;
    }
}

/// <summary>
/// Eine Gutschrift oder ein Abzug am Urlaubskonto eines Jahres -- Resturlaub
/// aus dem Vorjahr, ein geschenkter Tag, eine Korrektur.
/// </summary>
/// <remarks>
/// Eigene Zeilen statt einer veraenderten Zahl am Vertrag: Am Jahresende will
/// jemand wissen, warum es 32 Tage waren und nicht 30, und die Antwort steht
/// dann da.
/// </remarks>
public class LeaveAdjustment : Entity
{
    public required Guid ContactId { get; set; }

    public Contact? Contact { get; set; }

    public required int Year { get; set; }

    /// <summary>Positiv gibt Tage, negativ nimmt welche.</summary>
    public required decimal Days { get; set; }

    public required string Reason { get; set; }
}
