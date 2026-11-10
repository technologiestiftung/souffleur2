using Souffleur.Domain.Common;

namespace Souffleur.Domain.Contacts;

/// <summary>
/// Bankverbindung und Steuerangaben eines Kontakts. Getrennt gehalten, weil die
/// meisten Kontakte sie nicht haben -- eine Presseredaktion braucht keine IBAN.
/// </summary>
public class ContactBankDetails : Entity
{
    public Guid ContactId { get; set; }

    public Contact Contact { get; set; } = null!;

    public string? Iban { get; set; }

    public string? Bic { get; set; }

    /// <summary>Abweichender Kontoinhaber, falls das Konto nicht auf den Namen läuft.</summary>
    public string? AccountHolder { get; set; }

    public string? VatId { get; set; }

    /// <summary>Steuernummer, etwa für Honorarabrechnungen.</summary>
    public string? TaxNumber { get; set; }

    /// <summary>Zahlungsziel in Tagen.</summary>
    public int? PaymentTermDays { get; set; }

    /// <summary>Personenkonto beim Steuerbüro, Debitor oder Kreditor.</summary>
    public string? AccountNumber { get; set; }

    /// <summary>
    /// Ob Honorare an diese Person der Kuenstlersozialabgabe unterliegen.
    /// Die Einordnung trifft das Haus, nicht souffleur.
    /// </summary>
    public bool IsSubjectToArtistsSocialFund { get; set; }

    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];

        if (Iban is { Length: > 0 } iban)
        {
            string cleaned = iban.Replace(" ", string.Empty).ToUpperInvariant();

            if (cleaned.Length is < 15 or > 34)
            {
                problems.Add($"Die IBAN „{iban}“ hat eine unübliche Länge.");
            }
            else if (!cleaned[..2].All(char.IsAsciiLetterUpper) || !cleaned[2..].All(char.IsAsciiLetterOrDigit))
            {
                problems.Add($"Die IBAN „{iban}“ ist nicht richtig aufgebaut.");
            }
        }

        if (Bic is { Length: > 0 } bic && bic.Replace(" ", string.Empty).Length is not (8 or 11))
        {
            problems.Add($"Die BIC „{bic}“ hat weder 8 noch 11 Stellen.");
        }

        if (PaymentTermDays is < 0 or > 365)
        {
            problems.Add("Das Zahlungsziel liegt außerhalb von 0 bis 365 Tagen.");
        }

        return problems;
    }
}
