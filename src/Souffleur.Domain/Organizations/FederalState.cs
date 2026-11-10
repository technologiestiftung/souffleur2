namespace Souffleur.Domain.Organizations;

/// <summary>
/// Das Bundesland, in dem das Haus sitzt. Entscheidet ueber die Feiertage --
/// und damit darueber, wie viele Arbeitstage ein Urlaub kostet.
/// </summary>
/// <remarks>
/// Die Zahlen stehen in der Datenbank. Die Abkuerzungen sind die amtlichen
/// (ISO 3166-2:DE ohne Praefix).
/// </remarks>
public enum FederalState
{
    BW = 1,
    BY = 2,
    BE = 3,
    BB = 4,
    HB = 5,
    HH = 6,
    HE = 7,
    MV = 8,
    NI = 9,
    NW = 10,
    RP = 11,
    SL = 12,
    SN = 13,
    ST = 14,
    SH = 15,
    TH = 16,
}

public static class FederalStates
{
    public static readonly FederalState[] All = Enum.GetValues<FederalState>();

    public static string Label(this FederalState state) => state switch
    {
        FederalState.BW => "Baden-Württemberg",
        FederalState.BY => "Bayern",
        FederalState.BE => "Berlin",
        FederalState.BB => "Brandenburg",
        FederalState.HB => "Bremen",
        FederalState.HH => "Hamburg",
        FederalState.HE => "Hessen",
        FederalState.MV => "Mecklenburg-Vorpommern",
        FederalState.NI => "Niedersachsen",
        FederalState.NW => "Nordrhein-Westfalen",
        FederalState.RP => "Rheinland-Pfalz",
        FederalState.SL => "Saarland",
        FederalState.SN => "Sachsen",
        FederalState.ST => "Sachsen-Anhalt",
        FederalState.SH => "Schleswig-Holstein",
        FederalState.TH => "Thüringen",
        _ => state.ToString(),
    };
}
