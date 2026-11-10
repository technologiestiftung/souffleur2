using Souffleur.Domain.Common;

namespace Souffleur.Domain.Accounting;

/// <summary>
/// Eine Buchung im Journal. Traegt mindestens zwei Zeilen, deren Soll- und
/// Habensummen uebereinstimmen.
/// </summary>
/// <remarks>
/// Nach dem Festschreiben ist die Buchung unveraenderlich (<see cref="IImmutable"/>).
/// Korrigiert wird ausschliesslich durch eine Stornobuchung, die auf die
/// urspruengliche verweist -- GoBD, § 146 Abs. 4 AO.
/// </remarks>
public class JournalEntry : Entity, IImmutable
{
    /// <summary>
    /// Fortlaufende Nummer innerhalb des Geschaeftsjahres, lueckenlos. Wird beim
    /// Speichern vergeben, nicht beim Anlegen des Objekts.
    /// </summary>
    public int Number { get; set; }

    public required int FiscalYear { get; set; }

    /// <summary>Buchungsdatum -- wann der Vorgang wirtschaftlich stattfand.</summary>
    public required DateOnly Date { get; set; }

    public required string Description { get; set; }

    /// <summary>Belegnummer des Hauses, soweit vorhanden.</summary>
    public string? DocumentNumber { get; set; }

    public DateTimeOffset? CommittedAt { get; private set; }

    public Guid? CommittedBy { get; private set; }

    /// <summary>Gesetzt, wenn diese Buchung eine andere storniert.</summary>
    public Guid? ReversalOfId { get; set; }

    public JournalEntry? ReversalOf { get; set; }

    public List<JournalLine> Lines { get; set; } = [];

    public bool IsCommitted => CommittedAt is not null;

    public bool IsReversal => ReversalOfId is not null;

    public decimal DebitTotal => Lines.Sum(l => l.Debit);

    public decimal CreditTotal => Lines.Sum(l => l.Credit);

    /// <summary>
    /// Prueft, ob die Buchung buchbar ist. Gibt die Gruende zurueck, warum nicht --
    /// leer heisst in Ordnung.
    /// </summary>
    /// <param name="settings">
    /// Die Regeln des Hauses. Ohne Angabe gelten die Vorgaben; was von der
    /// Rechtsform abhaengt -- etwa die Sphaerenpflicht -- wird dann nicht geprueft.
    /// </param>
    public IReadOnlyList<string> Validate(AccountingSettings? settings = null)
    {
        List<string> problems = [];

        if (Lines.Count < 2)
        {
            problems.Add("Eine Buchung braucht mindestens zwei Zeilen.");
        }

        if (Lines.Count > 0 && DebitTotal != CreditTotal)
        {
            problems.Add(
                $"Soll und Haben stimmen nicht überein: {DebitTotal:N2} gegen {CreditTotal:N2}.");
        }

        if (DebitTotal == 0 && Lines.Count > 0)
        {
            problems.Add("Eine Buchung über null ist keine Buchung.");
        }

        if (Date.Year != FiscalYear)
        {
            problems.Add(
                $"Das Buchungsdatum {Date:dd.MM.yyyy} liegt nicht im Geschäftsjahr {FiscalYear}.");
        }

        problems.AddRange(Lines.SelectMany(line => line.Validate(settings)));

        return problems;
    }

    /// <summary>
    /// Schreibt die Buchung fest. Danach ist sie unveraenderlich.
    /// </summary>
    public void Commit(DateTimeOffset at, Guid? by, AccountingSettings? settings = null)
    {
        if (IsCommitted)
        {
            throw new InvalidOperationException(
                $"Buchung {Number} wurde bereits am {CommittedAt:dd.MM.yyyy} festgeschrieben.");
        }

        IReadOnlyList<string> problems = Validate(settings);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "Buchung lässt sich nicht festschreiben: " + string.Join(" ", problems));
        }

        CommittedAt = at;
        CommittedBy = by;
    }

    /// <summary>
    /// Erzeugt die Stornobuchung: dieselben Zeilen mit vertauschten Seiten.
    /// Die urspruengliche Buchung bleibt unangetastet im Journal stehen.
    /// </summary>
    public JournalEntry CreateReversal(DateOnly date, int fiscalYear, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Ein Storno braucht einen Grund.", nameof(reason));
        }

        return new JournalEntry
        {
            FiscalYear = fiscalYear,
            Date = date,
            Description = $"Storno zu Buchung {Number}: {reason}",
            DocumentNumber = DocumentNumber,
            ReversalOfId = Id,
            Lines = [.. Lines.Select(line => new JournalLine
            {
                AccountId = line.AccountId,
                // Vertauscht: Soll wird Haben und umgekehrt.
                Debit = line.Credit,
                Credit = line.Debit,
                TaxKey = line.TaxKey,
                TaxAmount = line.TaxAmount,
                CostCenterId = line.CostCenterId,
                ContactId = line.ContactId,
                Description = line.Description,
            })],
        };
    }
}
