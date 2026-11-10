using System.Globalization;
using System.Xml.Linq;
using Souffleur.Domain.Banking;

namespace Souffleur.Infrastructure.Banking;

/// <summary>Was in einem Kontoauszug steht.</summary>
/// <param name="Iban">Das Konto, zu dem der Auszug gehört.</param>
/// <param name="ClosingBalance">Der Schlusssaldo, wie die Bank ihn meldet.</param>
public sealed record BankStatement(
    string? Iban,
    string? StatementId,
    decimal? ClosingBalance,
    DateOnly? BalanceDate,
    IReadOnlyList<BankTransaction> Transactions);

public class Camt053Exception(string message) : Exception(message);

/// <summary>
/// Liest einen Kontoauszug im Format CAMT.053 (ISO 20022).
/// </summary>
/// <remarks>
/// Gelesen wird über die **lokalen** Namen der Elemente, nicht über den
/// Namensraum: Den gibt es in einem halben Dutzend Fassungen
/// (camt.053.001.02 bis .08), und die Banken liefern verschiedene. Am Aufbau
/// der Elemente ändert sich für das, was hier gebraucht wird, nichts.
///
/// Gelesen wird außerdem nur, was gebucht ist (<c>BOOK</c>). Vorgemerkte
/// Umsätze verschwinden wieder oder ändern ihren Betrag; wer sie übernimmt,
/// gleicht gegen etwas ab, das es noch nicht gibt.
/// </remarks>
public static class Camt053Reader
{
    public static BankStatement Read(Stream content)
    {
        XDocument document;

        try
        {
            document = XDocument.Load(content);
        }
        catch (System.Xml.XmlException exception)
        {
            throw new Camt053Exception($"Die Datei ist kein lesbares XML: {exception.Message}");
        }

        XElement? statement = Descendants(document.Root, "Stmt").FirstOrDefault();

        if (statement is null)
        {
            throw new Camt053Exception(
                "Kein Kontoauszug gefunden. Erwartet wird CAMT.053 mit einem <Stmt>-Abschnitt.");
        }

        string? iban = Value(Descendants(Child(statement, "Acct"), "IBAN").FirstOrDefault());

        (decimal? balance, DateOnly? balanceDate) = ClosingBalance(statement);

        List<BankTransaction> transactions =
        [
            .. Children(statement, "Ntry")
                .Select(ToTransaction)
                .OfType<BankTransaction>(),
        ];

        return new BankStatement(
            iban, Value(Child(statement, "Id")), balance, balanceDate, transactions);
    }

    /// <summary>
    /// Der Schlusssaldo. Die Bank liefert mehrere Salden; gesucht ist der mit
    /// dem Code <c>CLBD</c> -- oder, wenn es den nicht gibt, der letzte.
    /// </summary>
    private static (decimal?, DateOnly?) ClosingBalance(XElement statement)
    {
        List<XElement> balances = [.. Children(statement, "Bal")];

        XElement? closing = balances.FirstOrDefault(
            balance => Descendants(balance, "Cd").Any(code => code.Value.Trim() == "CLBD"))
            ?? balances.LastOrDefault();

        if (closing is null)
        {
            return (null, null);
        }

        decimal? amount = Amount(Child(closing, "Amt"));

        if (amount is not null
            && Value(Child(closing, "CdtDbtInd")) == "DBIT")
        {
            // Ein Sollsaldo ist ein Minus. Die Bank schreibt das Vorzeichen
            // nicht an den Betrag, sondern daneben.
            amount = -amount;
        }

        DateOnly? date = Date(Child(closing, "Dt"));
        return (amount, date);
    }

    private static BankTransaction? ToTransaction(XElement entry)
    {
        // Nur Gebuchtes. Vorgemerktes ändert sich noch.
        string? status = Value(Child(entry, "Sts")) ?? Value(Descendants(Child(entry, "Sts"), "Cd").FirstOrDefault());
        if (status is not null && status != "BOOK")
        {
            return null;
        }

        decimal? amount = Amount(Child(entry, "Amt"));
        DateOnly? booked = Date(Child(entry, "BookgDt"));

        if (amount is null || booked is null)
        {
            return null;
        }

        bool outgoing = Value(Child(entry, "CdtDbtInd")) == "DBIT";
        decimal signed = outgoing ? -amount.Value : amount.Value;

        XElement? details = Descendants(entry, "TxDtls").FirstOrDefault();

        // Die Gegenseite ist bei einer Belastung der Empfänger, bei einer
        // Gutschrift der Zahler.
        XElement? party = details is null
            ? null
            : Descendants(details, outgoing ? "Cdtr" : "Dbtr").FirstOrDefault();

        XElement? partyAccount = details is null
            ? null
            : Descendants(details, outgoing ? "CdtrAcct" : "DbtrAcct").FirstOrDefault();

        string? purpose = string.Join(' ',
            Descendants(entry, "Ustrd").Select(line => line.Value.Trim())
                .Where(line => line.Length > 0));

        string? counterpartyName = Value(Descendants(party, "Nm").FirstOrDefault());
        string? counterpartyIban = Value(Descendants(partyAccount, "IBAN").FirstOrDefault());

        return new BankTransaction
        {
            BankAccountId = Guid.Empty,
            BookedOn = booked.Value,
            ValueDate = Date(Child(entry, "ValDt")),
            Amount = signed,
            Currency = Attribute(Child(entry, "Amt"), "Ccy") ?? "EUR",
            Purpose = Empty(purpose),
            CounterpartyName = counterpartyName,
            CounterpartyIban = counterpartyIban,
            EndToEndId = Value(Descendants(entry, "EndToEndId").FirstOrDefault()),
            BankReference = Value(Child(entry, "AcctSvcrRef")),
            Fingerprint = BankTransaction.FingerprintOf(
                booked.Value, signed, purpose, counterpartyIban),
        };
    }

    // --- Kleinkram, ohne Namensraum ---

    private static IEnumerable<XElement> Children(XElement? parent, string name) =>
        parent?.Elements().Where(element => element.Name.LocalName == name) ?? [];

    private static XElement? Child(XElement? parent, string name) =>
        Children(parent, name).FirstOrDefault();

    private static IEnumerable<XElement> Descendants(XElement? parent, string name) =>
        parent?.Descendants().Where(element => element.Name.LocalName == name) ?? [];

    private static string? Value(XElement? element) => Empty(element?.Value.Trim());

    private static string? Empty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? Attribute(XElement? element, string name) =>
        element?.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value;

    private static decimal? Amount(XElement? element) =>
        element is not null
        && decimal.TryParse(
            element.Value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture,
            out decimal parsed)
            ? parsed
            : null;

    /// <remarks>
    /// Das Datum steckt je nach Bank in <c>&lt;Dt&gt;</c> oder in
    /// <c>&lt;DtTm&gt;</c>; die ersten zehn Zeichen tragen beides.
    /// </remarks>
    private static DateOnly? Date(XElement? element)
    {
        string? text = Value(Descendants(element, "Dt").FirstOrDefault())
            ?? Value(Descendants(element, "DtTm").FirstOrDefault())
            ?? Value(element);

        return text is { Length: >= 10 }
               && DateOnly.TryParse(
                   text[..10], CultureInfo.InvariantCulture, DateTimeStyles.None,
                   out DateOnly parsed)
            ? parsed
            : null;
    }
}
