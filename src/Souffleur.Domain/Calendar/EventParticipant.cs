using Souffleur.Domain.Catalogs;
using Souffleur.Domain.Common;
using Souffleur.Domain.Contacts;

namespace Souffleur.Domain.Calendar;

/// <summary>
/// Wie fest jemand für einen Termin eingeplant ist.
/// </summary>
/// <remarks>
/// Kein Einladungsverfahren: souffleur verschickt nichts. Die Disposition ruft
/// an und trägt ein, was dabei herauskam. Genau das ist im Kulturbetrieb der
/// Vorgang -- und ohne diese drei Stufen bliebe offen, ob eine Einteilung
/// steht oder nur gewünscht ist.
/// </remarks>
public enum ParticipationStatus
{
    /// <summary>Eingeteilt, noch nicht bestätigt.</summary>
    Assigned = 1,

    /// <summary>Hat zugesagt.</summary>
    Accepted = 2,

    /// <summary>Hat abgesagt. Bleibt stehen, damit nicht dieselbe Person
    /// zweimal gefragt wird.</summary>
    Declined = 3,
}

public static class ParticipationStatuses
{
    public static readonly ParticipationStatus[] All = Enum.GetValues<ParticipationStatus>();

    public static string Label(this ParticipationStatus status) => status switch
    {
        ParticipationStatus.Assigned => "Eingeteilt",
        ParticipationStatus.Accepted => "Zugesagt",
        ParticipationStatus.Declined => "Abgesagt",
        _ => status.ToString(),
    };

    /// <summary>
    /// Ob die Person durch diese Beteiligung belegt ist. Wer abgesagt hat, ist
    /// frei -- und kann anderswo eingeteilt werden, ohne dass es kollidiert.
    /// </summary>
    public static bool Occupies(this ParticipationStatus status) =>
        status is not ParticipationStatus.Declined;
}

/// <summary>
/// Dass jemand aus dem Adressbuch bei einem Termin oder einer Serie dabei ist.
/// </summary>
/// <remarks>
/// Aus den Kontakten und nicht aus einer eigenen Personenliste: Es gibt **ein**
/// Adressbuch (siehe <see cref="Contact"/>). Die Schauspielerin, die auf der
/// Probenliste steht, ist dieselbe, die am Monatsende ihr Honorar bekommt.
///
/// Wie bei der Ressourcenbelegung hängt der Eintrag entweder an einem Termin
/// oder an einer Serie -- die Probenserie hat ihre Einteilung, und der
/// einzelne Abend kann davon abweichen.
/// </remarks>
public class EventParticipant : Entity
{
    public required Guid ContactId { get; set; }

    public Contact? Contact { get; set; }

    public Guid? EventId { get; set; }

    public CalendarEvent? Event { get; set; }

    public Guid? SeriesId { get; set; }

    public EventSeries? Series { get; set; }

    /// <summary>
    /// In welcher Funktion -- Regie, Inspizienz, Abenddienst. Ein Katalog, weil
    /// jedes Haus das anders benennt (ADR-0005).
    /// </summary>
    /// <remarks>
    /// Nicht die Rolle aus dem Adressbuch: Dort steht, in welcher Eigenschaft
    /// jemand mit dem Haus zu tun hat. Hier steht, was er an diesem Abend tut.
    /// Dieselbe Person kann heute Inspizienz und morgen Abenddienst sein.
    /// </remarks>
    public Guid? FunctionId { get; set; }

    public CatalogEntry? Function { get; set; }

    public ParticipationStatus Status { get; set; } = ParticipationStatus.Assigned;

    public string? Note { get; set; }
}
