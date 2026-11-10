using Souffleur.Domain.Common;
using Souffleur.Domain.Contacts;

namespace Souffleur.Domain.Accounting;

/// <summary>
/// Ein Beleg: die Rechnung, der Kassenbon, die Quittung. Grundlage jeder Buchung --
/// keine Buchung ohne Beleg.
/// </summary>
/// <remarks>
/// Die Datei selbst wird nie veraendert und nie geloescht; sie liegt acht Jahre im
/// Datenverzeichnis (§ 147 Abs. 3 AO). Geaendert werden duerfen bis zur
/// Festschreibung nur die Angaben darum herum -- Lieferant, Betrag, Datum.
/// </remarks>
public class Voucher : Entity, IImmutable
{
    /// <summary>
    /// Fortlaufende Belegnummer des Hauses, lueckenlos je Jahr. Sie steht auf dem
    /// Beleg und verbindet Papier, Datei und Buchung.
    /// </summary>
    public int Number { get; set; }

    public required int FiscalYear { get; set; }

    /// <summary>Datum des Belegs, nicht des Hochladens.</summary>
    public required DateOnly Date { get; set; }

    /// <summary>Bruttobetrag, wie er auf dem Beleg steht.</summary>
    public required decimal GrossAmount { get; set; }

    public required string Description { get; set; }

    /// <summary>Rechnungsnummer des Lieferanten, soweit vorhanden.</summary>
    public string? ExternalNumber { get; set; }

    public DateOnly? DueDate { get; set; }

    public Guid? ContactId { get; set; }

    public Contact? Contact { get; set; }

    /// <summary>
    /// Zu welcher Produktion der Beleg gehoert. Daraus entstehen die
    /// Produktionskosten (ADR-0011). Keine Belegangabe im Sinne der GoBD --
    /// die Zuordnung darf sich auch nach dem Festschreiben noch aendern,
    /// siehe <c>AuditBehaviour</c>.
    /// </summary>
    public Guid? ProductionId { get; set; }

    public required VoucherDirection Direction { get; set; }

    public required VoucherSource Source { get; set; }

    /// <summary>Die hinterlegte Datei. Ein Beleg ohne Datei ist moeglich -- etwa ein
    /// Kassenbon, der nur im Ordner liegt --, aber die Regel ist die Datei.</summary>
    public VoucherFile? File { get; set; }

    public DateTimeOffset? CommittedAt { get; private set; }

    /// <summary>Die Buchung, die aus diesem Beleg entstanden ist.</summary>
    public Guid? JournalEntryId { get; set; }

    public JournalEntry? JournalEntry { get; set; }

    public bool IsBooked => JournalEntryId is not null;

    public string Label => $"{FiscalYear}-{Number:D5}";

    /// <summary>
    /// Schreibt den Beleg fest. Geschieht zusammen mit der Buchung, die auf ihm
    /// beruht -- danach aendert sich an beiden nichts mehr.
    /// </summary>
    public void Commit(DateTimeOffset at)
    {
        if (CommittedAt is not null)
        {
            throw new InvalidOperationException(
                $"Beleg {Label} wurde bereits am {CommittedAt:dd.MM.yyyy} festgeschrieben.");
        }

        CommittedAt = at;
    }

    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];

        if (GrossAmount <= 0)
        {
            problems.Add("Ein Beleg über null oder weniger ergibt keinen Sinn.");
        }

        if (Date.Year != FiscalYear)
        {
            problems.Add($"Das Belegdatum {Date:dd.MM.yyyy} liegt nicht im Geschäftsjahr {FiscalYear}.");
        }

        if (DueDate is { } due && due < Date)
        {
            problems.Add("Die Fälligkeit liegt vor dem Belegdatum.");
        }

        if (string.IsNullOrWhiteSpace(Description))
        {
            problems.Add("Ein Beleg braucht eine Bezeichnung.");
        }

        return problems;
    }
}

/// <summary>Eingehend oder ausgehend -- entscheidet über Kreditor oder Debitor.</summary>
public enum VoucherDirection
{
    /// <summary>Eingangsrechnung: Das Haus zahlt.</summary>
    Incoming = 1,

    /// <summary>Ausgangsrechnung oder Einnahmebeleg: Das Haus bekommt Geld.</summary>
    Outgoing = 2,
}

/// <summary>Woher der Beleg kam. Die GoBD verlangt, die Herkunft nachvollziehbar zu halten.</summary>
public enum VoucherSource
{
    /// <summary>Am Rechner hochgeladen.</summary>
    Upload = 1,

    /// <summary>Mit dem Handy abfotografiert.</summary>
    Photo = 2,

    /// <summary>Strukturierte E-Rechnung: XRechnung oder ZUGFeRD.</summary>
    ElectronicInvoice = 3,

    /// <summary>In souffleur geschrieben, etwa eine eigene Ausgangsrechnung.</summary>
    Generated = 4,

    /// <summary>Nur auf Papier vorhanden, hier lediglich erfasst.</summary>
    PaperOnly = 5,
}
