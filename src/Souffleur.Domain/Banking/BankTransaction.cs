using Souffleur.Domain.Accounting;
using Souffleur.Domain.Common;

namespace Souffleur.Domain.Banking;

/// <summary>
/// Ein Umsatz auf dem Kontoauszug.
/// </summary>
/// <remarks>
/// Was die Bank gemeldet hat, bleibt unverändert: Der Umsatz ist kein Vorgang
/// des Hauses, sondern eine Mitteilung. Geändert wird nur, was das Haus daran
/// hängt -- der Beleg, die Buchung, eine Notiz.
/// </remarks>
public class BankTransaction : Entity
{
    public required Guid BankAccountId { get; set; }

    public BankAccount? BankAccount { get; set; }

    /// <summary>Buchungstag der Bank.</summary>
    public required DateOnly BookedOn { get; set; }

    /// <summary>Wertstellung. Weicht oft um einen Tag ab.</summary>
    public DateOnly? ValueDate { get; set; }

    /// <summary>
    /// Vorzeichenbehaftet: positiv ist Geld herein, negativ hinaus. Die Bank
    /// liefert Betrag und Richtung getrennt; hier stehen sie zusammen, weil
    /// jede Auswertung sonst beides mitführen müsste.
    /// </summary>
    public required decimal Amount { get; set; }

    public string Currency { get; set; } = "EUR";

    /// <summary>Verwendungszweck, wie er auf dem Auszug steht.</summary>
    public string? Purpose { get; set; }

    public string? CounterpartyName { get; set; }

    public string? CounterpartyIban { get; set; }

    /// <summary>Ende-zu-Ende-Kennung aus der Überweisung, soweit vorhanden.</summary>
    public string? EndToEndId { get; set; }

    /// <summary>
    /// Die Referenz der Bank für diesen Umsatz. Daran erkennt der Import, was
    /// er schon kennt -- derselbe Auszug zweimal eingelesen darf die Umsätze
    /// nicht verdoppeln.
    /// </summary>
    public string? BankReference { get; set; }

    /// <summary>
    /// Fingerabdruck aus Datum, Betrag, Zweck und Gegenseite. Ersatz für die
    /// Bankreferenz, wo die Bank keine liefert -- und das kommt vor.
    /// </summary>
    public required string Fingerprint { get; set; }

    public Guid? VoucherId { get; set; }

    public Voucher? Voucher { get; set; }

    public Guid? JournalEntryId { get; set; }

    public JournalEntry? JournalEntry { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset ImportedAt { get; set; }

    /// <summary>Ob der Umsatz einem Beleg oder einer Buchung zugeordnet ist.</summary>
    public bool IsAssigned => VoucherId is not null || JournalEntryId is not null;

    public bool IsIncoming => Amount > 0;

    /// <summary>
    /// Der Fingerabdruck, wie ihn der Import bildet.
    /// </summary>
    /// <remarks>
    /// Bewusst ohne die Wertstellung: Manche Banken korrigieren sie nach, und
    /// dann hielte der Import denselben Umsatz für einen neuen.
    /// </remarks>
    public static string FingerprintOf(
        DateOnly bookedOn, decimal amount, string? purpose, string? counterpartyIban) =>
        string.Join('|',
            bookedOn.ToString("yyyy-MM-dd"),
            amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            Squeeze(purpose),
            Squeeze(counterpartyIban));

    /// <summary>
    /// Leerraum vereinheitlichen: Banken brechen den Verwendungszweck
    /// unterschiedlich um, und daran soll der Abgleich nicht scheitern.
    /// </summary>
    private static string Squeeze(string? value) =>
        value is null ? string.Empty
            : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    .ToUpperInvariant();
}
