using Souffleur.Domain.Common;

namespace Souffleur.Domain.Work;

/// <summary>
/// Dass eine Aufgabe erst beginnen kann, wenn eine andere fertig ist.
/// </summary>
/// <remarks>
/// Nur diese eine Art von Abhängigkeit. Die Projektplanung kennt vier
/// (Ende-Anfang, Anfang-Anfang, Ende-Ende, Anfang-Ende); gebraucht wird im
/// Kulturbetrieb die erste. „Die Technik kann einrichten, wenn das Bühnenbild
/// steht" ist der Fall. Die übrigen drei würden hier nur die Oberfläche
/// verkomplizieren, ohne dass jemand sie füllt.
///
/// Und: Der Zeitstrahl rechnet nichts um. Wer das Bühnenbild verschiebt,
/// verschiebt nicht automatisch die Einrichtung -- souffleur meldet nur, dass
/// die Reihenfolge nicht mehr aufgeht. Automatisches Umplanen ist die Stelle,
/// an der solche Werkzeuge unbenutzbar werden: Eine Verschiebung reißt zwanzig
/// Termine mit, die jemand mühsam abgestimmt hatte.
/// </remarks>
public class TaskDependency : Entity
{
    /// <summary>Die Aufgabe, die wartet.</summary>
    public required Guid TaskId { get; set; }

    public TaskItem? Task { get; set; }

    /// <summary>Die Aufgabe, auf die gewartet wird.</summary>
    public required Guid DependsOnId { get; set; }

    public TaskItem? DependsOn { get; set; }
}
