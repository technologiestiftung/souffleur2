namespace Souffleur.Domain.Personnel;

/// <summary>Das Urlaubskonto eines Jahres.</summary>
/// <param name="Entitlement">Aus dem Vertrag, anteilig, wenn er nicht das ganze Jahr galt.</param>
/// <param name="Adjustments">Resturlaub, geschenkte Tage, Korrekturen.</param>
/// <param name="Taken">Genehmigt.</param>
/// <param name="Pending">Beantragt und noch nicht entschieden.</param>
public sealed record LeaveBalance(
    int Year,
    decimal Entitlement,
    decimal Adjustments,
    decimal Taken,
    decimal Pending)
{
    /// <summary>Was bleibt, wenn alles Beantragte genehmigt wird.</summary>
    public decimal Remaining => Entitlement + Adjustments - Taken - Pending;
}

/// <summary>Das Stundenkonto eines Zeitraums, in Minuten.</summary>
/// <param name="Target">Was der Vertrag verlangt, an Arbeitstagen ohne Feiertag.</param>
/// <param name="Credited">Was Abwesenheiten gutschreiben -- Urlaub, Krankheit.</param>
/// <param name="Worked">Was gestempelt oder nachgetragen ist.</param>
/// <param name="MissingDays">Arbeitstage ohne Zeit und ohne Abwesenheit.</param>
public sealed record HoursBalance(
    DateOnly From,
    DateOnly To,
    int Target,
    int Credited,
    int Worked,
    IReadOnlyList<DateOnly> MissingDays)
{
    public int Balance => Worked + Credited - Target;
}

/// <summary>
/// Rechnet fuer eine Person: Welche Tage sind Arbeitstage, was kostet ein
/// Urlaub, wie steht das Stundenkonto.
/// </summary>
/// <remarks>
/// Ohne Datenbank, damit sich die Regeln einzeln pruefen lassen. Bekommt alles,
/// was es braucht: die Vertraege der Person, die freien Tage, ihre
/// Abwesenheiten.
///
/// souffleur rechnet hier, es entscheidet nicht. Ob der Urlaub anteilig nach
/// Monaten oder nach Tagen zusteht, ob Resturlaub verfaellt, ob ein Feiertag
/// am freien Tag nachgeholt wird -- das regeln Vertrag und Haus. Die Zahlen
/// sind der Ausgangspunkt fuer das Gespraech, nicht sein Ergebnis.
/// </remarks>
public sealed class WorkCalendar
{
    private readonly List<Employment> employments;
    private readonly HashSet<DateOnly> holidays;
    private readonly List<Absence> absences;

    public WorkCalendar(
        IEnumerable<Employment> employments,
        IEnumerable<DateOnly> holidays,
        IEnumerable<Absence> absences)
    {
        this.employments = [.. employments.OrderBy(e => e.ValidFrom)];
        this.holidays = [.. holidays];
        this.absences = [.. absences];
    }

    /// <summary>Welcher Vertrag an diesem Tag gilt -- der juengste, der schon begonnen hat.</summary>
    public Employment? EmploymentOn(DateOnly date)
    {
        Employment? current = employments.LastOrDefault(e => e.ValidFrom <= date);
        return current is { EndsOn: { } end } && end < date ? null : current;
    }

    /// <summary>Ob die Person an diesem Tag arbeiten muesste.</summary>
    public bool IsWorkDay(DateOnly date) =>
        EmploymentOn(date) is { } employment
        && employment.WorkDays.Includes(date.DayOfWeek)
        && !holidays.Contains(date);

    /// <summary>
    /// Wie viele Arbeitstage eine Abwesenheit kostet, auf ein Jahr begrenzt.
    /// </summary>
    /// <remarks>
    /// Beim Urlaub zaehlen Tage nicht, an denen die Person krank war und die
    /// Bescheinigung vorliegt (§ 9 BUrlG) -- wer im Urlaub krank wird, bekommt
    /// die Tage zurueck. Ohne Bescheinigung bleibt es beim Urlaub; das Haus
    /// sieht es und kann nachfragen.
    /// </remarks>
    public decimal DaysOf(Absence absence, int? year = null)
    {
        DateOnly from = absence.StartDate;
        DateOnly to = absence.EndDate;

        if (year is { } y)
        {
            from = Max(from, new DateOnly(y, 1, 1));
            to = Min(to, new DateOnly(y, 12, 31));
        }

        decimal days = 0;

        for (DateOnly date = from; date <= to; date = date.AddDays(1))
        {
            if (!IsWorkDay(date))
            {
                continue;
            }

            if (absence.Kind.CountsAgainstLeave() && IsCertifiedSick(date, absence.Id))
            {
                continue;
            }

            days += absence.DayShare;
        }

        return days;
    }

    /// <summary>
    /// Der Urlaubsanspruch eines Jahres. Gilt ein Vertrag nur einen Teil des
    /// Jahres, steht ihm der Anteil seiner Kalendertage zu -- auf halbe Tage
    /// aufgerundet.
    /// </summary>
    public decimal EntitlementOf(int year)
    {
        var first = new DateOnly(year, 1, 1);
        var last = new DateOnly(year, 12, 31);
        int daysInYear = last.DayNumber - first.DayNumber + 1;
        decimal total = 0;

        for (int i = 0; i < employments.Count; i++)
        {
            Employment employment = employments[i];
            DateOnly validTo = i + 1 < employments.Count
                ? employments[i + 1].ValidFrom.AddDays(-1)
                : DateOnly.MaxValue;

            if (employment.EndsOn is { } end && end < validTo)
            {
                validTo = end;
            }

            DateOnly from = Max(employment.ValidFrom, first);
            DateOnly to = Min(validTo, last);

            if (to < from)
            {
                continue;
            }

            int covered = to.DayNumber - from.DayNumber + 1;
            total += employment.AnnualLeaveDays * covered / daysInYear;
        }

        return Math.Ceiling(total * 2) / 2;
    }

    public LeaveBalance LeaveOf(int year, decimal adjustments)
    {
        decimal taken = 0;
        decimal pending = 0;

        foreach (Absence absence in absences.Where(a => a.Kind.CountsAgainstLeave()))
        {
            if (absence.Status == AbsenceStatus.Approved)
            {
                taken += DaysOf(absence, year);
            }
            else if (absence.Status == AbsenceStatus.Requested)
            {
                pending += DaysOf(absence, year);
            }
        }

        return new LeaveBalance(year, EntitlementOf(year), adjustments, taken, pending);
    }

    /// <summary>
    /// Das Stundenkonto zwischen zwei Tagen.
    /// </summary>
    /// <param name="workedByDay">Die gearbeiteten Minuten je Tag.</param>
    /// <remarks>
    /// Eine geltende Abwesenheit schreibt das Soll des Tages gut -- ausser dem
    /// Freizeitausgleich, der genau dafuer da ist, das Konto abzubauen. Ein
    /// halber Tag schreibt die Haelfte gut.
    /// </remarks>
    public HoursBalance HoursOf(
        DateOnly from,
        DateOnly to,
        IReadOnlyDictionary<DateOnly, int> workedByDay)
    {
        int target = 0;
        int credited = 0;
        int worked = 0;
        List<DateOnly> missing = [];

        for (DateOnly date = from; date <= to; date = date.AddDays(1))
        {
            int done = workedByDay.GetValueOrDefault(date);
            worked += done;

            if (!IsWorkDay(date))
            {
                continue;
            }

            int daily = EmploymentOn(date)!.DailyTargetMinutes;
            target += daily;

            decimal share = absences
                .Where(a => a.Status.IsInEffect() && a.Covers(date) && !a.Kind.ReducesHours())
                .Select(a => a.DayShare)
                .DefaultIfEmpty(0)
                .Max();

            credited += (int)Math.Round(daily * share);

            bool away = absences.Any(a => a.Status.IsInEffect() && a.Covers(date));
            if (done == 0 && !away)
            {
                missing.Add(date);
            }
        }

        return new HoursBalance(from, to, target, credited, worked, missing);
    }

    private bool IsCertifiedSick(DateOnly date, Guid except) =>
        absences.Any(a => a.Id != except
                          && a.Kind == AbsenceKind.Sick
                          && a.CertificateReceived
                          && a.Status.IsInEffect()
                          && a.Covers(date));

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
}
