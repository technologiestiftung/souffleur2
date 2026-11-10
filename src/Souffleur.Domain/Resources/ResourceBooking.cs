using Souffleur.Domain.Calendar;
using Souffleur.Domain.Common;

namespace Souffleur.Domain.Resources;

/// <summary>
/// Dass ein Termin oder eine Serie eine Ressource belegt.
/// </summary>
/// <remarks>
/// Eine Tabelle für beides, mit genau einem gesetzten Schlüssel: Eine Serie
/// belegt die Bühne an jedem ihrer Termine, und ein einzelner Termin belegt sie
/// einmal. Zwei Tabellen hätten jede Abfrage und jede Prüfung verdoppelt, ohne
/// dass sich fachlich etwas unterschiede.
///
/// Die Belegung einer Serie ist dabei eine Aussage über die Regel, nicht über
/// Datensätze: Welche Tage das sind, rechnet <see cref="EventSeries"/> aus.
/// </remarks>
public class ResourceBooking : Entity
{
    public required Guid ResourceId { get; set; }

    public Resource? Resource { get; set; }

    public Guid? EventId { get; set; }

    public CalendarEvent? Event { get; set; }

    public Guid? SeriesId { get; set; }

    public EventSeries? Series { get; set; }
}
