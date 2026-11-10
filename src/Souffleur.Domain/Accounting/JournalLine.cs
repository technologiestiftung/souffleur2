using Souffleur.Domain.Common;
using Souffleur.Domain.Contacts;

namespace Souffleur.Domain.Accounting;

/// <summary>
/// Eine Zeile einer Buchung. Genau eine Seite ist besetzt: entweder Soll oder Haben.
/// Mehrere Zeilen auf derselben Seite sind die Split-Buchung.
/// </summary>
public class JournalLine : Entity
{
    public Guid JournalEntryId { get; set; }

    public JournalEntry JournalEntry { get; set; } = null!;

    public required Guid AccountId { get; set; }

    public Account Account { get; set; } = null!;

    public decimal Debit { get; set; }

    public decimal Credit { get; set; }

    /// <summary>Steuerschluessel dieser Zeile, falls er vom Konto abweicht.</summary>
    public string? TaxKey { get; set; }

    /// <summary>
    /// Enthaltener Steuerbetrag. Getrennt gefuehrt, weil der DATEV-Stapel ihn
    /// einzeln braucht und Kulturbetriebe oft gemischte Umsaetze haben
    /// (§ 4 Nr. 20 UStG).
    /// </summary>
    public decimal TaxAmount { get; set; }

    /// <summary>
    /// Steuerliche Sphaere dieser Zeile. Pflicht bei gemeinnuetzigen Haeusern;
    /// im SKR42 steht sie nicht in der Kontonummer und muss hierher.
    /// </summary>
    public TaxSphere? TaxSphere { get; set; }

    public Guid? CostCenterId { get; set; }

    public CostCenter? CostCenter { get; set; }

    public Guid? ContactId { get; set; }

    public Contact? Contact { get; set; }

    public string? Description { get; set; }

    public decimal Amount => Debit > 0 ? Debit : Credit;

    public IReadOnlyList<string> Validate(AccountingSettings? settings = null)
    {
        List<string> problems = [];

        // Nur Erfolgskonten tragen eine Sphaere: Bei Bank und Kasse gibt es
        // nichts zuzuordnen, der Geldfluss selbst ist keiner Taetigkeit zugehoerig.
        if (settings?.RequiresTaxSpheres == true
            && TaxSphere is null
            && Account is { Type: AccountType.Revenue or AccountType.Expense })
        {
            problems.Add(
                $"Konto {Account.Label} braucht eine steuerliche Sphäre "
                + "(ideeller Bereich, Vermögensverwaltung, Zweckbetrieb oder "
                + "wirtschaftlicher Geschäftsbetrieb).");
        }

        if (Debit < 0 || Credit < 0)
        {
            problems.Add("Negative Beträge gibt es nicht; die Seite entscheidet über das Vorzeichen.");
        }

        if (Debit > 0 && Credit > 0)
        {
            problems.Add("Eine Zeile steht im Soll oder im Haben, nicht in beidem.");
        }

        if (Debit == 0 && Credit == 0)
        {
            problems.Add("Eine Zeile ohne Betrag gehört nicht in die Buchung.");
        }

        if (TaxAmount > Amount)
        {
            problems.Add($"Der Steuerbetrag {TaxAmount:N2} ist größer als die Zeile selbst.");
        }

        return problems;
    }
}
