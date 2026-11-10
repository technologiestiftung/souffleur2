namespace Souffleur.Infrastructure.Ticketing;

/// <summary>Ein Platz aus dem Saalplan.</summary>
/// <param name="Blocked">
/// Vom Haus gesperrt (Technik, Sichtbehinderung). Solche Plaetze sind nicht
/// verkaeuflich und gehoeren nicht in die Bezugsgroesse der Auslastung.
/// </param>
public record PretixSeat(bool Blocked);

/// <summary>Ein Kontingent mit den Produkten, die es abdeckt.</summary>
public record PretixQuota(int? Size, IReadOnlyList<int> Items, IReadOnlyList<int> Variations);

/// <summary>
/// Wie viele Plaetze eine Vorstellung hat.
/// </summary>
/// <remarks>
/// Eigene Klasse, weil hier die einzige echte Rechnung der Anbindung steckt --
/// alles andere ist Abfragen und Umschaufeln. Uebernommen aus dem alten
/// souffleur (`src/lib/server/pretix.ts`), dort entstanden an den Daten eines
/// Hauses.
/// </remarks>
public static class PretixCapacity
{
    /// <summary>
    /// Kapazitaet aus dem Saalplan.
    /// </summary>
    /// <remarks>
    /// Hat ein Event nummerierte Plaetze, ist deren Anzahl die exakte
    /// Saalkapazitaet -- dann braucht es keine Rechnung ueber Kontingente.
    /// </remarks>
    public static int? FromSeats(IReadOnlyList<PretixSeat> seats) =>
        seats.Count == 0 ? null : seats.Count(seat => !seat.Blocked);

    /// <summary>
    /// Kapazitaet aus den Kontingenten -- der Weg ohne Saalplan.
    /// </summary>
    /// <remarks>
    /// Der Normalfall sind Platzkategorien, die sich nicht ueberschneiden
    /// (Parkett, Rang, ...): Ihre Groessen werden addiert.
    ///
    /// pretix laesst aber zu, dass dasselbe Produkt in mehreren Kontingenten
    /// liegt -- etwa eine Gesamtquota "Saal 200" und darunter "Ermaessigt 30"
    /// fuer dieselben Tickets. Dann waeren 230 falsch. Unterschieden wird:
    ///
    /// <list type="bullet">
    /// <item>lauter ueberschneidungsfreie Kontingente -> Summe (der Regelfall)</item>
    /// <item>ein Kontingent, das alle anderen umfasst -> dessen Groesse ist die Grenze</item>
    /// <item>echte Teilueberlappung -> unbestimmt; lieber keine Zahl als eine falsche</item>
    /// </list>
    ///
    /// Ein unbegrenztes Kontingent macht die Auslastung grundsaetzlich
    /// unbestimmbar.
    /// </remarks>
    public static (int? Capacity, string? Note) FromQuotas(IReadOnlyList<PretixQuota> quotas)
    {
        if (quotas.Count == 0)
        {
            return (null, "Für diese Vorstellung ist kein Kontingent hinterlegt.");
        }

        if (quotas.Any(quota => quota.Size is null))
        {
            return (null, "Mindestens ein Kontingent ist unbegrenzt — eine Auslastung "
                + "lässt sich daraus nicht berechnen.");
        }

        List<HashSet<string>> covered = [.. quotas.Select(Covered)];

        // Deckt ein Kontingent alle Produkte der anderen ab, ist es die
        // bindende Grenze.
        for (int i = 0; i < quotas.Count; i++)
        {
            bool coversAll = covered
                .Where((_, j) => j != i)
                .All(other => covered[i].IsSupersetOf(other));

            if (coversAll)
            {
                return (quotas[i].Size, null);
            }
        }

        if (Disjoint(covered))
        {
            return (quotas.Sum(quota => quota.Size ?? 0), null);
        }

        return (null, $"Die {quotas.Count} Kontingente überschneiden sich teilweise — eine "
            + "eindeutige Saalkapazität ist daraus nicht ableitbar.");
    }

    /// <summary>Die Produkte eines Kontingents als vergleichbare Schluesselmenge.</summary>
    private static HashSet<string> Covered(PretixQuota quota) =>
    [
        .. quota.Items.Select(item => $"i{item}"),
        .. quota.Variations.Select(variation => $"v{variation}"),
    ];

    /// <summary>Ob sich keine zwei Kontingente ein Produkt teilen.</summary>
    private static bool Disjoint(List<HashSet<string>> covered)
    {
        for (int i = 0; i < covered.Count; i++)
        {
            for (int j = i + 1; j < covered.Count; j++)
            {
                if (covered[i].Overlaps(covered[j]))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
