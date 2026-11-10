using Souffleur.Domain.Common;
using Souffleur.Domain.Contacts;

namespace Souffleur.Domain.Personnel;

/// <summary>
/// Warum jemand nicht da ist.
/// </summary>
/// <remarks>
/// Die Arten unterscheiden sich in zwei Fragen, und nur diese beiden traegt
/// souffleur aus: Muss jemand zustimmen (<see cref="AbsenceKinds.NeedsApproval"/>),
/// und zehrt der Tag am Stundenkonto (<see cref="AbsenceKinds.ReducesHours"/>).
/// Was eine Art fuer die Lohnabrechnung bedeutet -- Entgeltfortzahlung,
/// Kinderkrankengeld, ruhendes Arbeitsverhaeltnis --, entscheidet die
/// Lohnbuchhaltung; souffleur sagt ihr nur, dass es den Fall gibt.
/// </remarks>
public enum AbsenceKind
{
    /// <summary>Erholungsurlaub. Zehrt am Urlaubsanspruch.</summary>
    Vacation = 1,

    /// <summary>Freizeitausgleich: ein freier Tag gegen Mehrstunden.</summary>
    TimeOffInLieu = 2,

    /// <summary>Bildungsurlaub nach Landesrecht.</summary>
    EducationLeave = 3,

    /// <summary>Sonderurlaub -- Umzug, Hochzeit, Todesfall.</summary>
    SpecialLeave = 4,

    /// <summary>Unbezahlter Urlaub.</summary>
    Unpaid = 5,

    /// <summary>Elternzeit. Das Arbeitsverhaeltnis ruht oder laeuft in Teilzeit weiter.</summary>
    ParentalLeave = 6,

    /// <summary>Arbeitsunfaehig.</summary>
    Sick = 7,

    /// <summary>Ein krankes Kind betreuen.</summary>
    ChildSick = 8,
}

public static class AbsenceKinds
{
    public static readonly AbsenceKind[] All = Enum.GetValues<AbsenceKind>();

    public static string Label(this AbsenceKind kind) => kind switch
    {
        AbsenceKind.Vacation => "Urlaub",
        AbsenceKind.TimeOffInLieu => "Freizeitausgleich",
        AbsenceKind.EducationLeave => "Bildungsurlaub",
        AbsenceKind.SpecialLeave => "Sonderurlaub",
        AbsenceKind.Unpaid => "Unbezahlter Urlaub",
        AbsenceKind.ParentalLeave => "Elternzeit",
        AbsenceKind.Sick => "Krank",
        AbsenceKind.ChildSick => "Kind krank",
        _ => kind.ToString(),
    };

    /// <summary>
    /// Ob jemand zustimmen muss. Krankheit wird gemeldet, nicht beantragt --
    /// niemand genehmigt ein Fieber.
    /// </summary>
    public static bool NeedsApproval(this AbsenceKind kind) => !kind.IsSickness();

    /// <summary>
    /// Ob die Art Gesundheitsdaten beruehrt (Art. 9 DSGVO). Solche Eintraege
    /// sehen nur die Betroffenen und wer Antraege entscheidet; sie tragen
    /// keine Notiz, damit niemand eine Diagnose hineinschreibt.
    /// </summary>
    public static bool IsSickness(this AbsenceKind kind) =>
        kind is AbsenceKind.Sick or AbsenceKind.ChildSick;

    /// <summary>
    /// Ob ein solcher Tag das Stundenkonto mindert. Nur der Freizeitausgleich
    /// tut das -- er ist genau dafuer da. Jede andere Abwesenheit macht den
    /// Tag neutral: Wer im Urlaub oder krank ist, schuldet keine Stunden.
    /// </summary>
    public static bool ReducesHours(this AbsenceKind kind) => kind == AbsenceKind.TimeOffInLieu;

    /// <summary>Ob die Art am Urlaubsanspruch zehrt.</summary>
    public static bool CountsAgainstLeave(this AbsenceKind kind) => kind == AbsenceKind.Vacation;

    /// <summary>
    /// Ob die Lohnbuchhaltung davon erfahren muss: Bei diesen Arten aendert
    /// sich etwas an der Zahlung oder an der Meldung an die Krankenkasse.
    /// </summary>
    public static bool ConcernsPayroll(this AbsenceKind kind) =>
        kind is AbsenceKind.Sick or AbsenceKind.ChildSick
            or AbsenceKind.Unpaid or AbsenceKind.ParentalLeave;
}

/// <summary>Wie weit ein Antrag ist.</summary>
public enum AbsenceStatus
{
    /// <summary>Gestellt, noch nicht entschieden.</summary>
    Requested = 1,

    /// <summary>Zugestimmt.</summary>
    Approved = 2,

    /// <summary>Abgelehnt. Bleibt stehen, damit nachlesbar ist, warum.</summary>
    Rejected = 3,

    /// <summary>Zurueckgezogen oder nachtraeglich aufgehoben.</summary>
    Withdrawn = 4,

    /// <summary>Krankheit: gemeldet. Braucht keine Zustimmung.</summary>
    Reported = 5,
}

public static class AbsenceStatuses
{
    public static string Label(this AbsenceStatus status) => status switch
    {
        AbsenceStatus.Requested => "Beantragt",
        AbsenceStatus.Approved => "Genehmigt",
        AbsenceStatus.Rejected => "Abgelehnt",
        AbsenceStatus.Withdrawn => "Zurückgezogen",
        AbsenceStatus.Reported => "Gemeldet",
        _ => status.ToString(),
    };

    /// <summary>
    /// Ob die Abwesenheit gilt: Die Person ist dann nicht da, der Kalender
    /// zeigt es, und wer sie einteilen will, bekommt einen Hinweis.
    /// </summary>
    public static bool IsInEffect(this AbsenceStatus status) =>
        status is AbsenceStatus.Approved or AbsenceStatus.Reported;

    /// <summary>Ob der Eintrag noch Platz belegt -- auch ein offener Antrag tut das.</summary>
    public static bool IsActive(this AbsenceStatus status) =>
        status is AbsenceStatus.Requested or AbsenceStatus.Approved or AbsenceStatus.Reported;
}

/// <summary>
/// Eine Abwesenheit: beantragt, entschieden oder -- bei Krankheit -- gemeldet.
/// </summary>
/// <remarks>
/// Ganze Tage, keine Uhrzeiten. Wer am Nachmittag zum Arzt geht, stempelt
/// eben frueher aus; eine Abwesenheit ist das, was im Kalender als Balken
/// steht. Ein halber Tag geht nur, wenn Anfang und Ende derselbe Tag sind.
///
/// Haengt am Kontakt, wie die Zeiten (ADR-0009): Dieselbe Person, die
/// Dienste hat und stempelt, ist die, die im Urlaub ist.
/// </remarks>
public class Absence : Entity
{
    public required Guid ContactId { get; set; }

    public Contact? Contact { get; set; }

    public required AbsenceKind Kind { get; set; }

    public AbsenceStatus Status { get; set; } = AbsenceStatus.Requested;

    public required DateOnly StartDate { get; set; }

    /// <summary>Der letzte Tag, einschliesslich.</summary>
    public required DateOnly EndDate { get; set; }

    /// <summary>Nur ein halber Tag. Geht nur bei einem einzelnen Tag.</summary>
    public bool IsHalfDay { get; set; }

    /// <summary>Wer in der Zeit vertritt.</summary>
    public Guid? DeputyId { get; set; }

    public Contact? Deputy { get; set; }

    /// <summary>
    /// Was die antragstellende Person dazu sagt. Bei Krankheit immer leer --
    /// eine Diagnose hat hier nichts zu suchen.
    /// </summary>
    public string? Note { get; set; }

    /// <summary>
    /// Ob die Arbeitsunfaehigkeitsbescheinigung vorliegt. Nur bei Krankheit.
    /// Mehr steht hier nicht: kein Arzt, kein Befund.
    /// </summary>
    public bool CertificateReceived { get; set; }

    /// <summary>Wer entschieden hat -- das Konto, nicht der Kontakt.</summary>
    public Guid? DecidedBy { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>Warum abgelehnt oder aufgehoben. Pflicht bei beidem.</summary>
    public string? DecisionNote { get; set; }

    /// <summary>
    /// Wann die Lohnbuchhaltung davon erfahren hat. souffleur schickt nichts
    /// selbst -- es merkt sich nur, dass jemand es getan hat.
    /// </summary>
    public DateTimeOffset? PayrollReportedAt { get; set; }

    /// <summary>Ob der Tag in den Zeitraum faellt.</summary>
    public bool Covers(DateOnly date) => date >= StartDate && date <= EndDate;

    /// <summary>Wie viel von einem Arbeitstag die Abwesenheit nimmt.</summary>
    public decimal DayShare => IsHalfDay ? 0.5m : 1m;

    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];

        if (ContactId == Guid.Empty)
        {
            problems.Add("Es fehlt, um wen es geht.");
        }

        if (EndDate < StartDate)
        {
            problems.Add("Das Ende liegt vor dem Anfang.");
        }

        if (IsHalfDay && EndDate != StartDate)
        {
            problems.Add("Ein halber Tag geht nur, wenn Anfang und Ende derselbe Tag sind.");
        }

        if (EndDate.DayNumber - StartDate.DayNumber > 366 * 3)
        {
            // Elternzeit kann drei Jahre dauern, mehr nicht. Was laenger ist,
            // ist fast immer ein vertipptes Jahr.
            problems.Add("Der Zeitraum ist länger als drei Jahre — ist das Jahr richtig?");
        }

        if (DeputyId is { } deputy && deputy == ContactId)
        {
            problems.Add("Niemand vertritt sich selbst.");
        }

        if (!Kind.IsSickness() && CertificateReceived)
        {
            problems.Add("Eine Bescheinigung gibt es nur bei Krankheit.");
        }

        return problems;
    }
}
