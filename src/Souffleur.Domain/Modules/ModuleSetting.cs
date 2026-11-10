using Souffleur.Domain.Common;

namespace Souffleur.Domain.Modules;

/// <summary>
/// Ob ein Modul in diesem Haus eingeschaltet ist. Ein Datensatz je Modul.
/// </summary>
public class ModuleSetting : Entity
{
    public required SouffleurModule Module { get; set; }

    public bool IsEnabled { get; set; }

    /// <summary>
    /// Warum es aus ist, in den Worten des Hauses -- etwa "tourt, die Karten
    /// verkaufen die Gastgeber". Steht spaeter in der Einstellungsseite.
    /// </summary>
    public string? Note { get; set; }

    public void Enable() => IsEnabled = true;

    public void Disable(string? note = null)
    {
        if (!Module.CanBeDisabled())
        {
            throw new InvalidOperationException(
                $"{Module.Label()} lässt sich nicht abschalten.");
        }

        IsEnabled = false;
        Note = note;
    }
}
