using Souffleur.Domain.Accounting;
using Souffleur.Domain.Common;

namespace Souffleur.Domain.Banking;

/// <summary>
/// Ein Konto des Hauses bei einer Bank.
/// </summary>
/// <remarks>
/// Getrennt vom Sachkonto der Buchhaltung, aber mit ihm verbunden: Das
/// Bankkonto trägt die IBAN und die Umsätze, das Sachkonto (1200 „Bank") trägt
/// die Buchungen. Wer beides in einen Topf wirft, kann einen Umsatz nicht mehr
/// von seiner Buchung unterscheiden -- und genau das ist der Abgleich.
/// </remarks>
public class BankAccount : Entity
{
    /// <summary>Wie das Haus es nennt: „Geschäftskonto", „Rücklage".</summary>
    public required string Name { get; set; }

    public required string Iban { get; set; }

    public string? Bic { get; set; }

    /// <summary>Die Bank, wie sie auf dem Auszug steht.</summary>
    public string? BankName { get; set; }

    /// <summary>
    /// Das Sachkonto, auf dem die Bewegungen dieses Kontos gebucht werden.
    /// </summary>
    public Guid? AccountId { get; set; }

    public Account? Account { get; set; }

    /// <summary>
    /// Der Saldo aus dem zuletzt eingelesenen Auszug, mit seinem Datum. Nicht
    /// gerechnet, sondern übernommen: Was die Bank sagt, gilt.
    /// </summary>
    public decimal? Balance { get; set; }

    public DateOnly? BalanceDate { get; set; }

    public bool IsActive { get; set; } = true;

    public List<BankTransaction> Transactions { get; set; } = [];

    /// <summary>Die IBAN, wie man sie liest: in Vierergruppen.</summary>
    public string IbanGrouped => Grouped(Iban);

    public static string Grouped(string iban) =>
        string.Join(' ', Normalize(iban).Chunk(4).Select(chunk => new string(chunk)));

    public static string Normalize(string iban) =>
        new string([.. iban.Where(char.IsLetterOrDigit)]).ToUpperInvariant();

    /// <summary>
    /// Prüft die IBAN nach ISO 13616: Die ersten vier Zeichen ans Ende, Buchstaben
    /// zu Zahlen, Rest bei Division durch 97 muss 1 sein.
    /// </summary>
    /// <remarks>
    /// Lohnt den Aufwand, weil ein Zahlendreher in der IBAN sonst erst auffällt,
    /// wenn das Geld woanders ist. Die Prüfziffer fängt genau das ab.
    /// </remarks>
    public static bool IsValidIban(string? iban)
    {
        if (iban is null)
        {
            return false;
        }

        string value = Normalize(iban);

        if (value.Length is < 15 or > 34
            || !char.IsAsciiLetterUpper(value[0])
            || !char.IsAsciiLetterUpper(value[1]))
        {
            return false;
        }

        string rearranged = string.Concat(value.AsSpan(4), value.AsSpan(0, 4));

        // Ziffernweise rechnen: Die Zahl wird sonst zu groß für jeden Typ.
        int remainder = 0;
        foreach (char character in rearranged)
        {
            if (char.IsAsciiDigit(character))
            {
                remainder = ((remainder * 10) + (character - '0')) % 97;
            }
            else if (char.IsAsciiLetterUpper(character))
            {
                remainder = ((remainder * 100) + (character - 'A' + 10)) % 97;
            }
            else
            {
                return false;
            }
        }

        return remainder == 1;
    }

    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];

        if (string.IsNullOrWhiteSpace(Name))
        {
            problems.Add("Ein Name ist nötig.");
        }

        if (!IsValidIban(Iban))
        {
            problems.Add($"„{Iban}“ ist keine gültige IBAN -- die Prüfziffer stimmt nicht.");
        }

        return problems;
    }
}
