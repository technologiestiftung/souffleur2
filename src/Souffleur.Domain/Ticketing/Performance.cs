namespace Souffleur.Domain.Ticketing;

/// <summary>
/// Eine einzelne Vorstellung mit ihren Verkaufszahlen.
/// </summary>
/// <remarks>
/// pretix kennt zwei Bauformen: Ein Event ist entweder selbst eine Vorstellung,
/// oder es ist eine Terminserie, und die Vorstellungen stecken als Subevents
/// darin. Theater legen Wiederaufnahmen in aller Regel als Serie an. Ohne diese
/// Unterscheidung erschiene eine ganze Spielserie als eine einzige Zeile -- mit
/// dem Datum des ersten Termins und der Summe aller Karten.
/// </remarks>
public record Performance(
    string Key,
    string Name,
    DateTimeOffset Date,

    /// <summary>Verfuegbare Plaetze; null, wenn sich keine eindeutige Kapazitaet ergibt.</summary>
    int? Capacity,

    /// <summary>Woher die Kapazitaet stammt -- damit sie nachvollziehbar bleibt.</summary>
    CapacitySource Source,

    /// <summary>Gebuchte Karten: bezahlt und verbindlich bestellt, ohne Stornos.</summary>
    int Sold,

    /// <summary>Auslastung in Prozent; null, wenn keine belastbare Kapazitaet vorliegt.</summary>
    double? Utilization,

    /// <summary>Erloes aus bezahlten Positionen.</summary>
    decimal Revenue,
    string Currency,

    /// <summary>Warum die Kapazitaet unbestimmt ist -- fuer den Hinweis in der Oberflaeche.</summary>
    string? CapacityNote);

public enum CapacitySource
{
    /// <summary>Unbestimmt.</summary>
    Unknown = 0,

    /// <summary>Aus dem Saalplan gezaehlt -- die genauere Quelle.</summary>
    SeatingPlan = 1,

    /// <summary>Aus den Kontingenten abgeleitet.</summary>
    Quotas = 2,
}
