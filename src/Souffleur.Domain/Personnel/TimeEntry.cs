using Souffleur.Domain.Calendar;
using Souffleur.Domain.Catalogs;
using Souffleur.Domain.Common;
using Souffleur.Domain.Contacts;

namespace Souffleur.Domain.Personnel;

/// <summary>Woher ein Zeiteintrag kommt.</summary>
/// <remarks>
/// Die Unterscheidung bleibt sichtbar, weil sie etwas bedeutet: Eine gestempelte
/// Zeit ist beim Kommen und Gehen entstanden, eine nachgetragene hinterher aus
/// der Erinnerung. Beides ist zulaessig, aber nicht dasselbe.
/// </remarks>
public enum TimeEntrySource
{
    /// <summary>Beim Kommen und Gehen gestempelt.</summary>
    Clock = 1,

    /// <summary>Nachgetragen -- vergessen zu stempeln, Dienst von auswaerts.</summary>
    Manual = 2,
}

public static class TimeEntrySources
{
    public static string Label(this TimeEntrySource source) => source switch
    {
        TimeEntrySource.Clock => "Gestempelt",
        TimeEntrySource.Manual => "Nachgetragen",
        _ => source.ToString(),
    };
}

/// <summary>
/// Eine gearbeitete Zeitspanne einer Person.
/// </summary>
/// <remarks>
/// Haengt am Kontakt, nicht am Benutzerkonto: Es gibt ein Adressbuch, und die
/// Person, die abends Dienst hat, ist dieselbe, die hier stempelt. Nicht jede
/// hat ein Konto -- die Aushilfe am Einlass stempelt nicht selbst, ihre Zeit
/// traegt die Disposition nach.
///
/// souffleur zeichnet auf. Ob die Aufzeichnung vollstaendig ist und ob die
/// Pausen genommen wurden, bleibt beim Haus: Das ist eine Frage der Organisation
/// und der Aufsicht, keine der Software.
/// </remarks>
public class TimeEntry : Entity
{
    public required Guid ContactId { get; set; }

    public Contact? Contact { get; set; }

    public required DateTimeOffset StartedAt { get; set; }

    /// <summary>Offen heisst: laeuft gerade.</summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>Pause in Minuten, von der Spanne abgezogen.</summary>
    public int BreakMinutes { get; set; }

    /// <summary>
    /// In welcher Funktion -- derselbe Katalog wie am Termin, damit „Abenddienst"
    /// hier und dort dasselbe heisst.
    /// </summary>
    public Guid? FunctionId { get; set; }

    public CatalogEntry? Function { get; set; }

    /// <summary>Zu welchem Termin, falls die Zeit an einem haengt.</summary>
    public Guid? EventId { get; set; }

    public CalendarEvent? Event { get; set; }

    public string? Note { get; set; }

    public required TimeEntrySource Source { get; set; }

    /// <summary>
    /// Warum der Eintrag nachtraeglich geaendert wurde. Pflicht bei jeder
    /// Korrektur: Eine stillschweigend verschobene Arbeitszeit ist der Grund,
    /// warum Aufzeichnungen angezweifelt werden.
    /// </summary>
    public string? CorrectionReason { get; set; }

    public bool IsRunning => EndedAt is null;

    /// <summary>
    /// Die anrechenbare Zeit in Minuten, Pause abgezogen. Offen: <c>null</c> --
    /// eine laufende Zeit ist keine Dauer, sondern ein Anfang.
    /// </summary>
    public int? WorkedMinutes => EndedAt is { } end
        ? Math.Max(0, (int)(end - StartedAt).TotalMinutes - BreakMinutes)
        : null;

    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];

        if (EndedAt is { } end)
        {
            if (end <= StartedAt)
            {
                problems.Add("Das Ende liegt vor dem Beginn.");
            }
            else if (end - StartedAt > TimeSpan.FromHours(24))
            {
                // Fast immer ein vergessenes Gehen. Durchzulassen hiesse, den
                // Monatssaldo mit einer Zahl zu fuellen, die niemand prueft.
                problems.Add("Die Spanne ist laenger als 24 Stunden — da fehlt ein Ende.");
            }
            else if (BreakMinutes >= (end - StartedAt).TotalMinutes)
            {
                problems.Add("Die Pause ist so lang wie die Spanne oder laenger.");
            }
        }

        if (BreakMinutes < 0)
        {
            problems.Add("Eine Pause kann nicht negativ sein.");
        }

        return problems;
    }

    /// <summary>
    /// Was auffaellt, ohne dass es den Eintrag ungueltig macht.
    /// </summary>
    /// <remarks>
    /// Bewusst Hinweise und keine Fehler. Das Arbeitszeitgesetz richtet sich an
    /// den Arbeitgeber, nicht an die Erfassung: Eine Schicht von elf Stunden hat
    /// stattgefunden, auch wenn sie nicht haette stattfinden duerfen. Sie zu
    /// verschweigen waere schlechter, als sie zu melden.
    /// </remarks>
    public IReadOnlyList<string> Notices()
    {
        if (WorkedMinutes is not { } minutes)
        {
            return [];
        }

        List<string> notices = [];

        if (minutes > 10 * 60)
        {
            notices.Add(
                $"{Format(minutes)} an einem Tag — das Arbeitszeitgesetz lässt höchstens "
                + "10 Stunden zu (§ 3 ArbZG).");
        }

        if (minutes > 9 * 60 && BreakMinutes < 45)
        {
            notices.Add("Über 9 Stunden stehen 45 Minuten Pause zu (§ 4 ArbZG).");
        }
        else if (minutes > 6 * 60 && BreakMinutes < 30)
        {
            notices.Add("Über 6 Stunden stehen 30 Minuten Pause zu (§ 4 ArbZG).");
        }

        return notices;
    }

    /// <summary>Minuten als „7:30 Stunden" -- so steht es auf jedem Stundenzettel.</summary>
    public static string Format(int minutes) =>
        $"{minutes / 60}:{Math.Abs(minutes % 60):D2} Stunden";
}
