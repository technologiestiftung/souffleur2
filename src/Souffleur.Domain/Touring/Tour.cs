using Souffleur.Domain.Catalogs;
using Souffleur.Domain.Common;
using Souffleur.Domain.Productions;

namespace Souffleur.Domain.Touring;

/// <summary>
/// Eine Tournee: eine Produktion, die das Haus zu anderen bringt -- als
/// Klammer ueber ihre Gastspiele.
/// </summary>
/// <remarks>
/// Zwei Ebenen, nicht eine: Das tourende Kollektiv denkt in Reisen („die
/// Herbsttournee mit X"), die Buchhaltung in Gastgebern („die Gage aus
/// Hamburg"). Die Tournee traegt, was fuer alle Stationen gilt -- Produktion
/// und Reisende --, das Gastspiel das, was je Ort anders ist: Spielstaette,
/// Termine, Gage, Stand der Verhandlung.
///
/// Eine Tournee mit einem einzigen Gastspiel ist erlaubt und der haeufige
/// Fall. Die Klammer kostet dann nichts weiter als einen Titel.
///
/// Touring ist ein Modul neben den anderen und steht trotzdem auf ihnen:
/// Spielstaette und Ansprechpersonen sind Kontakte, Vorstellungen erscheinen
/// im Kalender, die Gage haengt an einem Beleg, die Reisenden sind
/// Beteiligte mit Funktion wie an jedem Termin. Nichts davon wird hier ein
/// zweites Mal gefuehrt.
/// </remarks>
public class Tour : Entity
{
    public required string Title { get; set; }

    /// <summary>
    /// Welche Produktion reist -- aus den Produktionen des Hauses (ADR-0011).
    /// Darf fehlen, solange die Tournee erst eine Absicht ist.
    /// </summary>
    public Guid? ProductionId { get; set; }

    public Production? Production { get; set; }

    public string? Notes { get; set; }

    public List<TourStop> Stops { get; set; } = [];

    /// <summary>
    /// Wer mitreist. Gilt fuer alle Gastspiele der Tournee -- das Ensemble
    /// faehrt mit, nicht einzeln je Ort. Wer an einer Station fehlt, wird im
    /// Gastspiel vermerkt, nicht hier gestrichen.
    /// </summary>
    public List<TourMember> Members { get; set; } = [];

    /// <summary>Erste Anreise und letzte Abreise ueber alle Gastspiele.</summary>
    public (DateOnly From, DateOnly To)? Span
    {
        get
        {
            List<DateOnly> days =
            [
                .. Stops.Where(s => s.Status != TourStopStatus.Cancelled)
                    .SelectMany(s => new[] { s.ArrivalDate, s.DepartureDate })
                    .Where(d => d is not null)
                    .Select(d => d!.Value),
                .. Stops.Where(s => s.Status != TourStopStatus.Cancelled)
                    .SelectMany(s => s.Performances)
                    .Select(p => DateOnly.FromDateTime(p.StartsAt.UtcDateTime)),
            ];

            return days.Count == 0 ? null : (days.Min(), days.Max());
        }
    }

    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];

        if (string.IsNullOrWhiteSpace(Title))
        {
            problems.Add("Eine Tournee braucht einen Titel.");
        }

        if (Members.Select(m => m.ContactId).Distinct().Count() != Members.Count)
        {
            problems.Add("Dieselbe Person reist zweimal mit.");
        }

        return problems;
    }
}

/// <summary>
/// Jemand aus dem Adressbuch, der mit auf Tournee geht -- in welcher Funktion.
/// </summary>
/// <remarks>
/// Dasselbe Muster wie die Beteiligung am Termin (<c>EventParticipant</c>):
/// Kontakt, Funktion aus dem Katalog, Stand der Zusage. Im Kalender stehen
/// die Reisenden deshalb an jeder Vorstellung der Tournee, und der Dienstplan
/// zaehlt sie wie jede andere Einteilung.
/// </remarks>
public class TourMember : Entity
{
    public Guid TourId { get; set; }

    public Tour Tour { get; set; } = null!;

    public required Guid ContactId { get; set; }

    public Contacts.Contact? Contact { get; set; }

    /// <summary>Was die Person auf der Reise tut -- Katalog „Funktionen am Termin".</summary>
    public Guid? FunctionId { get; set; }

    public CatalogEntry? Function { get; set; }

    public Calendar.ParticipationStatus Status { get; set; } = Calendar.ParticipationStatus.Assigned;

    public string? Note { get; set; }
}
