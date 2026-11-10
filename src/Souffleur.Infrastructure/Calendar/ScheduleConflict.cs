using Souffleur.Domain.Calendar;

namespace Souffleur.Infrastructure.Calendar;

/// <summary>
/// Was sich nicht zweimal zugleich vergeben lässt.
/// </summary>
public enum ConflictSubject
{
    /// <summary>Ein Raum, ein Fahrzeug, ein Gerät.</summary>
    Resource = 1,

    /// <summary>
    /// Jemand aus dem Adressbuch. Immer exklusiv -- niemand ist an zwei Orten.
    /// </summary>
    Person = 2,
}

/// <summary>
/// Ein Termin, der dieselbe Bühne oder dieselbe Person zur selben Zeit belegt.
/// </summary>
/// <param name="SubjectName">
/// Der Name der Bühne oder der Person -- was in der Meldung steht.
/// </param>
/// <param name="OccurrenceStart">
/// Bei einem Serienvorkommen der von der Regel errechnete Zeitpunkt -- damit
/// die Oberfläche genau dieses Vorkommen aufschlagen kann.
/// </param>
public sealed record ScheduleConflict(
    ConflictSubject Subject,
    Guid SubjectId,
    string SubjectName,
    DateOnly LocalDate,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Title,
    Guid? EventId,
    Guid? SeriesId,
    DateTimeOffset? OccurrenceStart,
    EventStatus Status);

/// <summary>
/// Die Bühne oder die Person ist zu dieser Zeit schon vergeben.
/// </summary>
/// <remarks>
/// Eigener Typ und nicht eine weitere Meldung in der
/// <see cref="CalendarException"/>: Ein Konflikt ist kein Eingabefehler. Er
/// lässt sich übergehen -- die Disposition weiß manchmal, dass sie die Bühne
/// doppelt belegt, und löst es später auf. Deshalb trägt er seine Gründe als
/// Daten und nicht als Sätze.
/// </remarks>
public class ScheduleConflictException(IReadOnlyList<ScheduleConflict> conflicts)
    : Exception(Describe(conflicts))
{
    public IReadOnlyList<ScheduleConflict> Conflicts { get; } = conflicts;

    private static string Describe(IReadOnlyList<ScheduleConflict> conflicts)
    {
        string names = string.Join(", ", conflicts.Select(c => c.SubjectName).Distinct());
        return conflicts.Count == 1
            ? $"{names} ist zu dieser Zeit schon vergeben."
            : $"{names}: {conflicts.Count} Überschneidungen.";
    }
}
