using Souffleur.Domain.Catalogs;
using Souffleur.Domain.Common;

namespace Souffleur.Domain.Resources;

/// <summary>
/// Etwas, das ein Termin belegt: die Bühne, die Probebühne, die Werkstatt, der
/// Transporter.
/// </summary>
/// <remarks>
/// Eine eigene Entität und kein Katalog (ADR-0005), weil eine Ressource mehr
/// trägt als ihren Namen: Von <see cref="IsExclusive"/> hängt ab, ob zwei
/// Termine nebeneinander dürfen, und daran rechnet die Belegungsprüfung. Ein
/// Katalogeintrag trägt Name, Farbe und Reihenfolge -- mehr nicht.
///
/// Die *Art* der Ressource ist dagegen ein Katalog: Ob ein Haus „Probebühne“
/// und „Fundus“ führt oder „Studio A“ und „Lager“, entscheidet es selbst.
/// </remarks>
public class Resource : Entity
{
    public required string Name { get; set; }

    /// <summary>
    /// Kurzzeichen für enge Spalten -- „PB1“ statt „Probebühne 1“. Im
    /// Jahresraster ist für mehr kein Platz.
    /// </summary>
    public string? ShortName { get; set; }

    /// <summary>Die Art: Bühne, Probebühne, Werkstatt -- ein Katalogeintrag.</summary>
    public Guid? KindId { get; set; }

    public CatalogEntry? Kind { get; set; }

    public string? Description { get; set; }

    /// <summary>Plätze, wo das Haus welche zählt. Sonst leer.</summary>
    public int? Capacity { get; set; }

    /// <summary>
    /// Ob die Ressource nur einmal gleichzeitig vergeben werden kann.
    /// </summary>
    /// <remarks>
    /// Für einen Raum stimmt das immer: Zwei Proben im selben Saal sind ein
    /// Fehler, kein Sonderfall. Für einen Scheinwerferpark oder einen Pool
    /// gleichartiger Mikrofone stimmt es nicht -- deshalb abschaltbar und
    /// nicht fest verdrahtet. Nur exklusive Ressourcen lösen eine Warnung aus.
    /// </remarks>
    public bool IsExclusive { get; set; } = true;

    public int SortOrder { get; set; }

    /// <summary>
    /// Abgeschaltet heißt: nicht mehr auswählbar, aber an Terminen, die sie
    /// schon tragen, weiter lesbar. Dasselbe Prinzip wie bei den Listen des
    /// Hauses -- eine Bühne, die es nicht mehr gibt, macht den Spielplan von
    /// 2023 sonst unlesbar.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public List<ResourceBooking> Bookings { get; set; } = [];

    /// <summary>Was in engen Spalten steht: das Kurzzeichen, sonst der Name.</summary>
    public string Display => string.IsNullOrWhiteSpace(ShortName) ? Name : ShortName;
}
