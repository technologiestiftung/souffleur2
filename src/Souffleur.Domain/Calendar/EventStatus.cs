namespace Souffleur.Domain.Calendar;

/// <summary>
/// Wie fest ein Termin steht.
/// </summary>
/// <remarks>
/// Die Unterscheidung zwischen vorgemerkt und fest ist im Kulturbetrieb keine
/// Feinheit: Eine Option auf einen Gastspieltermin blockiert den Saal, zählt
/// aber nicht als Vorstellung, und wer beides gleich darstellt, verplant
/// entweder den Raum doppelt oder lässt ihn leer stehen.
/// </remarks>
public enum EventStatus
{
    /// <summary>Vorgemerkt, Option, noch nicht zugesagt.</summary>
    Planned = 1,

    /// <summary>Fest.</summary>
    Confirmed = 2,

    /// <summary>
    /// Abgesagt. Bleibt stehen, statt zu verschwinden -- sonst weiß am Montag
    /// niemand mehr, warum der Samstag frei ist.
    /// </summary>
    Cancelled = 3,
}

public static class EventStatuses
{
    public static readonly EventStatus[] All = Enum.GetValues<EventStatus>();

    public static string Label(this EventStatus status) => status switch
    {
        EventStatus.Planned => "Vorgemerkt",
        EventStatus.Confirmed => "Fest",
        EventStatus.Cancelled => "Abgesagt",
        _ => status.ToString(),
    };
}
