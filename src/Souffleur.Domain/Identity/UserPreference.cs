using Souffleur.Domain.Common;

namespace Souffleur.Domain.Identity;

/// <summary>
/// Eine Einstellung, die einer Person gehoert und nicht dem Haus -- etwa die
/// Reihenfolge, in der sie ihre Reiter sortiert hat.
/// </summary>
/// <remarks>
/// Schluessel und Wert als Text, nicht je Einstellung eine Spalte. Was eine
/// Person fuer sich einstellt, kommt einzeln und unvorhersehbar dazu; jede
/// Kleinigkeit waere sonst eine Migration.
///
/// Der Preis dafuer: Die Datenbank kennt die Bedeutung nicht und kann nichts
/// pruefen. Deshalb ist der Wert hier nie Pflichtangabe fuer irgendetwas --
/// fehlt er oder steht Unsinn darin, faellt die Oberflaeche auf ihre
/// gelieferte Reihenfolge zurueck. Eine Einstellung darf kaputtgehen, ohne
/// dass jemand ausgesperrt wird.
/// </remarks>
public class UserPreference : Entity
{
    public required Guid UserId { get; set; }

    /// <summary>
    /// Woran die Einstellung haengt, etwa <c>module</c> fuer die Reiter oben
    /// oder <c>aktionen:Belege</c> fuer die Knoepfe auf der Belegseite.
    /// </summary>
    public required string Key { get; set; }

    public required string Value { get; set; }
}
