using Souffleur.Domain.Calendar;

namespace Souffleur.Infrastructure.Calendar;

/// <summary>
/// Woher ein Eintrag im Kalender stammt. Der Kalender führt zusammen, was
/// anderswo entsteht -- siehe ADR-0006.
/// </summary>
public enum CalendarSource
{
    /// <summary>Im Kalender angelegt. Nur diese Einträge sind dort änderbar.</summary>
    Calendar = 1,

    /// <summary>Frist einer Aufgabe.</summary>
    Tasks = 2,

    /// <summary>Vorstellung aus dem Spielplan des Hauses.</summary>
    Artwork = 3,

    /// <summary>Veranstaltung mit Vorverkauf.</summary>
    Ticketing = 4,

    /// <summary>Gastspiel einer Tournee: Reise und Vorstellungen anderswo.</summary>
    Touring = 5,

    /// <summary>
    /// Jemand ist nicht da. Nur das, nicht warum: Den Grund sehen die Person
    /// selbst und wer Antraege entscheidet -- im Kalender steht er fuer alle.
    /// </summary>
    Absence = 6,
}

/// <summary>
/// Eine Ressource, wie sie am Termin erscheint.
/// </summary>
/// <param name="Display">Das Kurzzeichen, wo es eines gibt -- sonst der Name.</param>
public sealed record OccurrenceResource(Guid Id, string Name, string Display, string? Color);

/// <summary>
/// Jemand, der bei einem Termin dabei ist, wie er am Termin erscheint.
/// </summary>
/// <param name="FunctionId">
/// Die Funktion als Kennung, nicht nur als Beschriftung. Der Dienstplan zaehlt
/// damit gegen den Bedarf -- ueber die Beschriftung zu vergleichen hiesse, sich
/// darauf zu verlassen, dass niemand sie umbenennt.
/// </param>
public sealed record OccurrenceParticipant(
    Guid ContactId,
    string Name,
    Guid? FunctionId,
    string? Function,
    ParticipationStatus Status,
    string StatusLabel);

/// <summary>
/// Eine gewünschte Einteilung, so wie sie hereinkommt.
/// </summary>
/// <remarks>
/// Mehr als eine Id, weil eine Beteiligung mehr trägt als „ist dabei": in
/// welcher Funktion, und ob schon zugesagt wurde.
/// </remarks>
public sealed record ParticipantInput(
    Guid ContactId,
    Guid? FunctionId = null,
    ParticipationStatus Status = ParticipationStatus.Assigned,
    string? Note = null);

/// <summary>
/// Ein Eintrag im Kalender, wie ihn die Oberfläche bekommt: entweder ein
/// einzelner Termin oder ein ausgerechnetes Vorkommen einer Serie.
/// </summary>
/// <param name="EventId">Gesetzt, wenn dahinter ein Termin des Kalenders liegt.</param>
/// <param name="SourceId">
/// Der Datensatz in der Quelle, aus der der Eintrag stammt -- bei einer Frist
/// die Aufgabe. Die Oberfläche braucht ihn, um dorthin zu springen; ändern
/// lässt sich der Eintrag nur dort, wo er entsteht (ADR-0006).
/// </param>
/// <param name="OccurrenceStart">
/// Bei einem Serienvorkommen der von der Regel errechnete Zeitpunkt. Damit
/// lässt sich genau dieses Vorkommen ändern oder absagen.
/// </param>
/// <param name="LocalDate">
/// Der Tag in der Zeitzone des Hauses. Das Jahresraster rechnet mit Tagen,
/// nicht mit Zeitpunkten -- und die Umrechnung gehört auf den Server, damit
/// nicht jede Ansicht sie neu erfindet.
/// </param>
public sealed record CalendarOccurrence(
    Guid? EventId,
    Guid? SourceId,
    Guid? SeriesId,
    DateTimeOffset? OccurrenceStart,
    string Title,
    string? Description,
    string? Location,
    Guid? CategoryId,
    string? CategoryLabel,
    string? CategoryColor,
    EventStatus Status,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateOnly LocalDate,
    DateOnly LocalEndDate,
    bool IsAllDay,
    bool IsFromSeries,
    CalendarSource Source,
    IReadOnlyList<OccurrenceResource> Resources,
    IReadOnlyList<OccurrenceParticipant> Participants)
{
    /// <summary>Über wie viele Tage sich der Eintrag im Raster erstreckt.</summary>
    public int SpanDays => LocalEndDate.DayNumber - LocalDate.DayNumber + 1;
}
