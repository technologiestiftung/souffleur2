using Souffleur.Domain.Common;
using Souffleur.Domain.Organizations;

namespace Souffleur.Domain.Personnel;

/// <summary>Ein Tag, an dem niemand Soll hat.</summary>
/// <param name="IsHouseDay">
/// Vom Haus geschenkt und nicht gesetzlich -- Heiligabend, Silvester, der Tag
/// nach der Premiere.
/// </param>
public sealed record Holiday(DateOnly Date, string Name, bool IsHouseDay = false);

/// <summary>
/// Die gesetzlichen Feiertage eines Bundeslands.
/// </summary>
/// <remarks>
/// Gerechnet und nicht aus einer Tabelle geladen, damit das naechste Jahr ohne
/// Update stimmt. Einmalige Feiertage (Berlin 2020 und 2025: 8. Mai) stehen
/// ausdruecklich drin. Was ein Land neu einfuehrt, muss hier nachgetragen
/// werden -- deshalb gibt es die Tage des Hauses (<see cref="HouseHoliday"/>)
/// als Ausweg, bis das geschehen ist.
///
/// Regional begrenzte Feiertage (Augsburger Friedensfest, Mariae Himmelfahrt in
/// Teilen Bayerns) fehlen: Sie gelten nicht im ganzen Land, und welche Gemeinde
/// ein Haus hat, weiss souffleur nicht.
/// </remarks>
public static class PublicHolidays
{
    public static List<Holiday> Of(int year, FederalState state)
    {
        DateOnly easter = EasterSunday(year);
        List<Holiday> days =
        [
            new(new DateOnly(year, 1, 1), "Neujahr"),
            new(easter.AddDays(-2), "Karfreitag"),
            new(easter.AddDays(1), "Ostermontag"),
            new(new DateOnly(year, 5, 1), "Tag der Arbeit"),
            new(easter.AddDays(39), "Christi Himmelfahrt"),
            new(easter.AddDays(50), "Pfingstmontag"),
            new(new DateOnly(year, 10, 3), "Tag der Deutschen Einheit"),
            new(new DateOnly(year, 12, 25), "1. Weihnachtsfeiertag"),
            new(new DateOnly(year, 12, 26), "2. Weihnachtsfeiertag"),
        ];

        if (state is FederalState.BW or FederalState.BY or FederalState.ST)
        {
            days.Add(new(new DateOnly(year, 1, 6), "Heilige Drei Könige"));
        }

        if ((state == FederalState.BE && year >= 2019) || (state == FederalState.MV && year >= 2023))
        {
            days.Add(new(new DateOnly(year, 3, 8), "Internationaler Frauentag"));
        }

        if (state == FederalState.BE && year is 2020 or 2025)
        {
            days.Add(new(new DateOnly(year, 5, 8), "Tag der Befreiung"));
        }

        if (state is FederalState.BW or FederalState.BY or FederalState.HE
            or FederalState.NW or FederalState.RP or FederalState.SL)
        {
            days.Add(new(easter.AddDays(60), "Fronleichnam"));
        }

        if (state == FederalState.SL)
        {
            days.Add(new(new DateOnly(year, 8, 15), "Mariä Himmelfahrt"));
        }

        if (state == FederalState.TH && year >= 2019)
        {
            days.Add(new(new DateOnly(year, 9, 20), "Weltkindertag"));
        }

        // Seit 2018 auch in Bremen, Hamburg, Niedersachsen und
        // Schleswig-Holstein; vorher nur im Osten.
        if (state is FederalState.BB or FederalState.MV or FederalState.SN
                or FederalState.ST or FederalState.TH
            || (year >= 2018 && state is FederalState.HB or FederalState.HH
                or FederalState.NI or FederalState.SH))
        {
            days.Add(new(new DateOnly(year, 10, 31), "Reformationstag"));
        }

        if (state is FederalState.BW or FederalState.BY or FederalState.NW
            or FederalState.RP or FederalState.SL)
        {
            days.Add(new(new DateOnly(year, 11, 1), "Allerheiligen"));
        }

        if (state == FederalState.SN)
        {
            // Der Mittwoch vor dem 23. November.
            var day = new DateOnly(year, 11, 22);
            while (day.DayOfWeek != DayOfWeek.Wednesday)
            {
                day = day.AddDays(-1);
            }

            days.Add(new(day, "Buß- und Bettag"));
        }

        return [.. days.OrderBy(d => d.Date)];
    }

    /// <summary>Ostersonntag nach der gregorianischen Osterformel (Meeus/Jones/Butcher).</summary>
    public static DateOnly EasterSunday(int year)
    {
        int a = year % 19;
        int b = year / 100;
        int c = year % 100;
        int d = b / 4;
        int e = b % 4;
        int f = (b + 8) / 25;
        int g = (b - f + 1) / 3;
        int h = ((19 * a) + b - d - g + 15) % 30;
        int i = c / 4;
        int k = c % 4;
        int l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        int m = (a + (11 * h) + (22 * l)) / 451;
        int month = (h + l - (7 * m) + 114) / 31;
        int day = ((h + l - (7 * m) + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }
}

/// <summary>
/// Ein freier Tag, den das Haus allen gibt -- jedes Jahr am selben Datum.
/// </summary>
/// <remarks>
/// Heiligabend und Silvester sind keine gesetzlichen Feiertage, aber in vielen
/// Haeusern frei. Wer sie als Urlaub nehmen muesste, haette zwei Tage weniger,
/// und das war nicht gemeint.
/// </remarks>
public class HouseHoliday : Entity
{
    public required int Month { get; set; }

    public required int Day { get; set; }

    public required string Name { get; set; }

    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];

        if (string.IsNullOrWhiteSpace(Name))
        {
            problems.Add("Der Tag braucht einen Namen.");
        }

        // Im Schaltjahr gerechnet, damit der 29. Februar durchgeht -- er
        // faellt in den anderen Jahren eben aus.
        if (Month is < 1 or > 12 || Day < 1 || Day > DateTime.DaysInMonth(2024, Month))
        {
            problems.Add("Das Datum gibt es nicht.");
        }

        return problems;
    }
}
