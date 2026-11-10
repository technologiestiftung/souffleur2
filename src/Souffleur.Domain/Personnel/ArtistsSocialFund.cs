using Souffleur.Domain.Common;

namespace Souffleur.Domain.Personnel;

/// <summary>
/// Der Abgabesatz der Kuenstlersozialkasse fuer ein Jahr.
/// </summary>
/// <remarks>
/// Je Jahr ein Satz, weil er sich jaehrlich aendert. Souffleur liefert keinen
/// mit: Der Satz wird jaehrlich im Bundesanzeiger veroeffentlicht, und eine
/// Zahl, die in der Software steht, waere spaetestens im naechsten Jahr falsch,
/// ohne dass es jemand merkt. Das Haus traegt ihn ein.
/// </remarks>
public class ArtistsSocialFundRate : Entity
{
    public required int Year { get; set; }

    /// <summary>Prozent vom Entgelt, etwa 5.0.</summary>
    public required decimal Percent { get; set; }

    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];

        if (Year is < 2000 or > 2100)
        {
            problems.Add("Das Jahr liegt außerhalb des Erwartbaren.");
        }

        if (Percent is < 0 or > 100)
        {
            problems.Add("Ein Abgabesatz liegt zwischen 0 und 100 Prozent.");
        }

        return problems;
    }
}

/// <summary>Was an eine Person gezahlt wurde, und woraus die Zahl stammt.</summary>
/// <param name="ContactId">Die Person im Adressbuch.</param>
/// <param name="Name">Ihr Name, wie er dort steht.</param>
/// <param name="Amount">Summe der Entgelte des Jahres.</param>
/// <param name="VoucherCount">Aus wie vielen Belegen.</param>
/// <param name="UnbookedCount">
/// Wie viele davon noch nicht gebucht sind. Bei denen steht der Bruttobetrag --
/// die Umsatzsteuer liesse sich erst aus der Buchung herausrechnen.
/// </param>
public record ArtistPayments(
    Guid ContactId,
    string Name,
    decimal Amount,
    int VoucherCount,
    int UnbookedCount);

/// <summary>
/// Die Meldung an die Kuenstlersozialkasse, soweit souffleur sie rechnen kann.
/// </summary>
/// <remarks>
/// Souffleur rechnet zusammen, was das Haus gekennzeichnet hat. Ob jemand
/// abgabepflichtig ist, entscheidet nicht die Software: Das haengt an der
/// Selbstaendigkeit, an der Art der Leistung und manchmal an einer
/// Einzelfallpruefung der Kasse. Deshalb steht neben der Summe immer, wer
/// *nicht* gekennzeichnet ist, aber kuenstlerisch mit dem Haus zu tun hat --
/// damit die Frage gestellt wird und nicht uebersehen.
/// </remarks>
/// <param name="Rate">Der hinterlegte Satz, oder null.</param>
/// <param name="Base">Bemessungsgrundlage: die Summe der Entgelte.</param>
/// <param name="Levy">Die Abgabe, oder null ohne Satz.</param>
/// <param name="Flagged">Wer gekennzeichnet ist, mit seinen Entgelten.</param>
/// <param name="Unflagged">
/// Wer kuenstlerisch mit dem Haus zu tun hat, Geld bekommen hat und *nicht*
/// gekennzeichnet ist.
/// </param>
public record ArtistsSocialFundReport(
    int Year,
    decimal? Rate,
    decimal Base,
    decimal? Levy,
    IReadOnlyList<ArtistPayments> Flagged,
    IReadOnlyList<ArtistPayments> Unflagged)
{
    public int UnbookedCount => Flagged.Sum(a => a.UnbookedCount);
}
