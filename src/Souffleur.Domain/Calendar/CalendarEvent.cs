using Souffleur.Domain.Common;
using Souffleur.Domain.Resources;

namespace Souffleur.Domain.Calendar;

/// <summary>
/// Ein Termin: entweder einzeln, oder die Abweichung einer Serie von ihrer Regel.
/// </summary>
/// <remarks>
/// Beides in einer Tabelle, weil es dasselbe ist: ein Termin mit Titel, Zeit und
/// Kategorie. Der Unterschied liegt allein in <see cref="SeriesId"/> -- ist sie
/// gesetzt, ersetzt dieser Datensatz das Vorkommen der Serie, das zum Zeitpunkt
/// in <see cref="OccurrenceStart"/> gehört.
///
/// So bleibt die Serie eine Regel (ADR-0006) und trotzdem lässt sich der eine
/// Dienstag verschieben, an dem die Bühne belegt ist.
/// </remarks>
public class CalendarEvent : Entity
{
    public required string Title { get; set; }

    public string? Description { get; set; }

    public string? Location { get; set; }

    public Guid? CategoryId { get; set; }

    /// <summary>Zu welcher Produktion der Termin gehoert -- Probe, Vorstellung, Bauprobe (ADR-0011).</summary>
    public Guid? ProductionId { get; set; }

    public EventStatus Status { get; set; } = EventStatus.Confirmed;

    public required DateTimeOffset StartsAt { get; set; }

    public required DateTimeOffset EndsAt { get; set; }

    /// <summary>
    /// Ganztägig. Die Uhrzeiten bleiben trotzdem gesetzt (00:00 bis 23:59 der
    /// Ortszeit), damit Sortierung und Zeitraumsuche nicht zwei Fälle brauchen.
    /// </summary>
    public bool IsAllDay { get; set; }

    /// <summary>Gesetzt, wenn dieser Termin eine Serie an einer Stelle abändert.</summary>
    public Guid? SeriesId { get; set; }

    public EventSeries? Series { get; set; }

    /// <summary>
    /// Welches Vorkommen der Serie dieser Datensatz ersetzt -- der Zeitpunkt,
    /// den die Regel ausgerechnet hätte. Entspricht RECURRENCE-ID in RFC 5545.
    /// </summary>
    /// <remarks>
    /// Nicht der neue Zeitpunkt: Wird die Probe von Dienstag auf Mittwoch
    /// verschoben, steht hier weiter der Dienstag. Sonst fände die Auflösung
    /// das Vorkommen nicht, das sie ausblenden soll, und der Termin stünde
    /// zweimal im Raster.
    /// </remarks>
    public DateTimeOffset? OccurrenceStart { get; set; }

    /// <summary>
    /// Was dieser Termin belegt -- Bühne, Probebühne, Transporter.
    /// </summary>
    /// <remarks>
    /// Auch an der Abweichung einer Serie, und zwar mit eigenem Bestand: Wird
    /// die Probe in einen anderen Raum verlegt, soll sie den alten freigeben.
    /// </remarks>
    public List<ResourceBooking> Bookings { get; set; } = [];

    /// <summary>Wer dabei ist -- aus dem Adressbuch.</summary>
    public List<EventParticipant> Participants { get; set; } = [];

    public bool IsException => SeriesId is not null;

    public TimeSpan Duration => EndsAt - StartsAt;
}
