using System.Globalization;
using System.Text;
using Souffleur.Domain.Accounting;
using Souffleur.Domain.Organizations;

namespace Souffleur.Infrastructure.Accounting.Datev;

/// <summary>
/// Erzeugt einen Buchungsstapel im DATEV-Format EXTF.
/// </summary>
/// <remarks>
/// Aufbau: Zeile 1 ist der Kopf mit Berater, Mandant und Zeitraum, Zeile 2 traegt
/// die Spaltennamen, ab Zeile 3 steht je Buchungszeile ein Satz. Getrennt wird
/// mit Semikolon, Text steht in Anfuehrungszeichen.
///
/// **Windows-1252, nicht UTF-8.** DATEV liest den Stapel als ANSI; in UTF-8
/// wuerden aus Umlauten zwei Zeichen, und "Buehnenbild" kaeme als "BÃ¼hnenbild"
/// beim Steuerbuero an.
///
/// Die Feldbelegung stammt aus oeffentlich dokumentierten Umsetzungen des
/// Formats, nicht aus der Spezifikation von DATEV selbst. Bevor ein Haus damit
/// arbeitet, muss ein Testexport beim Steuerbuero eingelesen werden -- siehe
/// docs/datev-export.md.
/// </remarks>
public class DatevBuchungsstapel(DatevOptions options)
{
    /// <summary>Anzahl der Felder je Zeile im Format 700, Formatversion 12.</summary>
    private const int FieldCount = 116;

    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>
    /// Baut den Stapel. Gibt die Bytes zurueck, wie sie in die Datei gehoeren.
    /// </summary>
    public byte[] Build(
        IReadOnlyList<JournalEntry> entries,
        Organization organization,
        DateOnly from,
        DateOnly to,
        DateTimeOffset now)
    {
        var builder = new StringBuilder();

        builder.Append(BuildHeader(organization, from, to, now)).Append("\r\n");
        builder.Append(BuildColumnNames()).Append("\r\n");

        foreach (JournalEntry entry in entries.OrderBy(e => e.Number))
        {
            foreach (string line in BuildEntry(entry))
            {
                builder.Append(line).Append("\r\n");
            }
        }

        return Windows1252.GetBytes(builder.ToString());
    }

    public static Encoding Windows1252 => CodePages.Get(1252);

    /// <summary>
    /// Zeile 1: der Kopf. Die Feldfolge ist fest; leere Felder bleiben leer,
    /// werden aber mitgezaehlt.
    /// </summary>
    private string BuildHeader(Organization organization, DateOnly from, DateOnly to, DateTimeOffset now)
    {
        string label = Shorten($"souffleur {from:MM.yyyy}", 30);

        string[] fields =
        [
            Quote("EXTF"),                                  //  1 Kennzeichen
            "700",                                          //  2 Versionsnummer
            "21",                                           //  3 Formatkategorie: Buchungsstapel
            Quote("Buchungsstapel"),                        //  4 Formatname
            "12",                                           //  5 Formatversion
            now.ToString("yyyyMMddHHmmssfff"),              //  6 erzeugt am
            string.Empty, string.Empty,                     //  7, 8 frei
            string.Empty, string.Empty,                     //  9, 10 frei
            options.ConsultantNumber.ToString(),            // 11 Beraternummer
            options.ClientNumber.ToString(),                // 12 Mandantennummer
            $"{from.Year:D4}0101",                          // 13 Beginn des Wirtschaftsjahres
            options.AccountLength.ToString(),               // 14 Sachkontenlänge
            from.ToString("yyyyMMdd"),                      // 15 Datum von
            to.ToString("yyyyMMdd"),                        // 16 Datum bis
            Quote(label),                                   // 17 Bezeichnung
            Quote(Shorten(options.Initials ?? string.Empty, 2)), // 18 Diktatkürzel
            "1",                                            // 19 Buchungstyp: Finanzbuchführung
            "0",                                            // 20 Rechnungslegungszweck
            "1",                                            // 21 Festschreibung: ja
            Quote("EUR"),                                   // 22 Währungskennzeichen
            string.Empty,                                   // 23 frei
            string.Empty,                                   // 24 Derivatskennzeichen
            string.Empty, string.Empty,                     // 25, 26 frei
            Quote(SachkontenrahmenOf(organization)),        // 27 Sachkontenrahmen
            string.Empty,                                   // 28 Branchenlösung
            string.Empty, string.Empty,                     // 29, 30 frei
            Quote(Shorten($"souffleur {from:MM/yyyy}", 16)),// 31 Anwendungsinformation
        ];

        return string.Join(';', fields);
    }

    /// <summary>
    /// Der Kontenrahmen, nach dem das Haus bucht -- so, wie DATEV ihn erwartet.
    /// </summary>
    private static string SachkontenrahmenOf(Organization organization) =>
        organization.ChartOfAccounts switch
        {
            ChartOfAccounts.Skr03 => "03",
            ChartOfAccounts.Skr04 => "04",
            ChartOfAccounts.Skr42 => "42",
            _ => string.Empty,
        };

    /// <summary>
    /// Zeile 2: die Spaltennamen. Nur die belegten sind benannt; die uebrigen
    /// bleiben leer, muessen aber dastehen, damit die Spaltenzahl stimmt.
    /// </summary>
    private static string BuildColumnNames()
    {
        string[] names = new string[FieldCount];
        Array.Fill(names, string.Empty);

        Set(names, 1, "Umsatz (ohne Soll/Haben-Kz)");
        Set(names, 2, "Soll/Haben-Kennzeichen");
        Set(names, 3, "WKZ Umsatz");
        Set(names, 7, "Konto");
        Set(names, 8, "Gegenkonto (ohne BU-Schlüssel)");
        Set(names, 9, "BU-Schlüssel");
        Set(names, 10, "Belegdatum");
        Set(names, 11, "Belegfeld 1");
        Set(names, 14, "Buchungstext");
        Set(names, 21, "Beleginfo - Art 1");
        Set(names, 22, "Beleginfo - Inhalt 1");
        Set(names, 37, "KOST1 - Kostenstelle");
        Set(names, 38, "KOST2 - Kostenstelle");
        Set(names, 114, "Festschreibung");

        return string.Join(';', names.Select(Quote));

        static void Set(string[] target, int position, string value) => target[position - 1] = value;
    }

    /// <summary>
    /// Eine Buchung wird zu so vielen Saetzen, wie sie Zeilen hat.
    /// </summary>
    /// <remarks>
    /// DATEV kennt Soll und Gegenkonto je Satz. Eine einfache Buchung mit zwei
    /// Zeilen waere auch als ein Satz darstellbar; eine Splitbuchung nicht. Damit
    /// beides gleich aussieht, wird jede Zeile einzeln uebergeben -- gegen das
    /// Sammelkonto der jeweils anderen Seite.
    /// </remarks>
    private IEnumerable<string> BuildEntry(JournalEntry entry)
    {
        // Die Gegenseite: Bei einer Buchung mit genau zwei Zeilen ist es die
        // jeweils andere, sonst bleibt das Feld leer und DATEV verteilt über
        // die Sätze desselben Belegfelds.
        foreach (JournalLine line in entry.Lines)
        {
            JournalLine? counter = entry.Lines.Count == 2
                ? entry.Lines.First(l => l != line)
                : null;

            // Jede Zeile einmal: Nur die besetzte Seite liefert den Satz, sonst
            // stünde jeder Betrag doppelt im Stapel.
            if (entry.Lines.Count == 2 && line != entry.Lines[0])
            {
                continue;
            }

            string[] fields = new string[FieldCount];
            Array.Fill(fields, string.Empty);

            Set(fields, 1, Amount(line.Amount));
            Set(fields, 2, Quote(line.Debit > 0 ? "S" : "H"));
            Set(fields, 3, Quote("EUR"));
            Set(fields, 7, line.Account.Number);
            Set(fields, 8, counter?.Account.Number ?? string.Empty);
            Set(fields, 9, line.TaxKey ?? string.Empty);
            Set(fields, 10, entry.Date.ToString("ddMM"));
            Set(fields, 11, Quote(Shorten(entry.DocumentNumber ?? entry.Number.ToString(), 36)));
            Set(fields, 14, Quote(Shorten(line.Description ?? entry.Description, 60)));

            // Die steuerliche Sphäre hat im Format kein eigenes Feld. Sie geht als
            // Beleginfo mit, damit sie nicht verlorengeht; wohin sie beim
            // Steuerbüro gehört, ist mit ihm zu klären.
            if (line.TaxSphere is { } sphere)
            {
                Set(fields, 21, Quote("Sphäre"));
                Set(fields, 22, Quote(sphere.Label()));
            }

            Set(fields, 37, Quote(line.CostCenter?.Number ?? string.Empty));
            Set(fields, 114, "1");

            yield return string.Join(';', fields);
        }

        static void Set(string[] target, int position, string value) => target[position - 1] = value;
    }

    /// <summary>Betrag mit Komma und zwei Nachkommastellen, immer ohne Vorzeichen.</summary>
    private static string Amount(decimal value) =>
        Math.Abs(value).ToString("0.00", German);

    private static string Quote(string value) =>
        $"\"{value.Replace("\"", "\"\"")}\"";

    private static string Shorten(string value, int length) =>
        value.Length <= length ? value : value[..length];
}

/// <summary>
/// Haelt die Windows-Codepages bereit. .NET bringt ausserhalb von Windows nur
/// Unicode mit; ohne diese Registrierung gaebe es Windows-1252 nicht.
/// </summary>
internal static class CodePages
{
    private static bool registered;

    public static Encoding Get(int codePage)
    {
        if (!registered)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            registered = true;
        }

        return Encoding.GetEncoding(codePage);
    }
}
