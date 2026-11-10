using Souffleur.Domain.Common;

namespace Souffleur.Domain.Accounting;

/// <summary>
/// Eine Übergabe ans Steuerbüro. Jeder Export wird festgehalten, weil mit ihm
/// die enthaltenen Buchungen festgeschrieben werden.
/// </summary>
/// <remarks>
/// Der Datensatz ist das Gegenstück zur Festschreibung: Fragt jemand, warum eine
/// Buchung nicht mehr änderbar ist, steht hier wann, von wem und in welcher Datei
/// sie das Haus verlassen hat.
/// </remarks>
public class DatevExport : Entity
{
    public required int FiscalYear { get; set; }

    public required DateOnly From { get; set; }

    public required DateOnly To { get; set; }

    /// <summary>Dateiname, unter dem die Übergabe erzeugt wurde.</summary>
    public required string FileName { get; set; }

    /// <summary>Wie viele Buchungen enthalten sind.</summary>
    public required int EntryCount { get; set; }

    /// <summary>Summe der Sollseite, zur Kontrolle gegen das, was ankommt.</summary>
    public required decimal DebitTotal { get; set; }

    /// <summary>SHA-256 der erzeugten Datei.</summary>
    public required string Sha256 { get; set; }

    public required DateTimeOffset ExportedAt { get; set; }

    public Guid? ExportedBy { get; set; }

    public string Label => $"{From:dd.MM.yyyy}–{To:dd.MM.yyyy}";
}
