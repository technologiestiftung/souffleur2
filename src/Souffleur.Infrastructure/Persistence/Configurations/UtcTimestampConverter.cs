using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Souffleur.Infrastructure.Persistence.Configurations;

/// <summary>
/// Speichert Zeitstempel als ISO-8601 in UTC.
/// </summary>
/// <remarks>
/// SQLite kennt keinen Zeitstempeltyp; EF Core lehnt <c>ORDER BY</c> auf
/// <c>DateTimeOffset</c> deshalb ab. Als Text in UTC ist die Reihenfolge der
/// Zeichen dieselbe wie die der Zeitpunkte -- damit sortiert und vergleicht die
/// Datenbank richtig.
///
/// Die Umrechnung nach UTC ist nicht nur Formsache: Mit unterschiedlichen
/// Zeitzonenversatzen waere "2026-03-01T01:00:00+02:00" kleiner als
/// "2026-03-01T00:30:00+00:00", obwohl es spaeter liegt. Beim Jahreswechsel
/// eines Buchungszeitraums waere das ein echter Fehler.
/// </remarks>
public class UtcTimestampConverter() : ValueConverter<DateTimeOffset, string>(
    value => value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture),
    text => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal));

public class NullableUtcTimestampConverter() : ValueConverter<DateTimeOffset?, string?>(
    value => value == null
        ? null
        : value.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture),
    text => text == null
        ? null
        : DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal));
