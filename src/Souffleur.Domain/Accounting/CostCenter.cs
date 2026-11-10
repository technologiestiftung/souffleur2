using Souffleur.Domain.Common;

namespace Souffleur.Domain.Accounting;

/// <summary>
/// Kostenstelle des Hauses -- Verwaltung, Technik, Marketing. Jede Buchungszeile
/// traegt eine; daraus entsteht spaeter Plan gegen Ist je Bereich.
/// </summary>
public class CostCenter : Entity
{
    public required string Number { get; set; }

    public required string Name { get; set; }

    public bool IsActive { get; set; } = true;

    public string Label => $"{Number} {Name}";
}
