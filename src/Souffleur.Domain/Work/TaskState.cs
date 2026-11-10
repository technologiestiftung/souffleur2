namespace Souffleur.Domain.Work;

/// <summary>
/// Wo eine Aufgabe steht.
/// </summary>
/// <remarks>
/// Vier Stufen und nicht mehr. „Wartet" ist dabei die, die man am ehesten
/// weglassen würde und am wenigsten entbehren kann: Im Kulturbetrieb hängt
/// vieles an einer Zusage von außen -- der Förderstelle, der Spielstätte, der
/// Agentur. Ohne eigene Stufe steht so etwas als „offen" in der Liste und
/// sieht aus wie Versäumnis.
/// </remarks>
public enum TaskState
{
    Open = 1,

    InProgress = 2,

    /// <summary>Liegt bei jemand anderem.</summary>
    Waiting = 3,

    Done = 4,

    /// <summary>
    /// Fällt weg. Bleibt stehen statt gelöscht zu werden -- sonst fragt in
    /// drei Monaten jemand, was eigentlich aus der Sache geworden ist.
    /// </summary>
    Dropped = 5,
}

public static class TaskStates
{
    public static readonly TaskState[] All = Enum.GetValues<TaskState>();

    public static string Label(this TaskState state) => state switch
    {
        TaskState.Open => "Offen",
        TaskState.InProgress => "In Arbeit",
        TaskState.Waiting => "Wartet",
        TaskState.Done => "Erledigt",
        TaskState.Dropped => "Verworfen",
        _ => state.ToString(),
    };

    /// <summary>Ob die Aufgabe noch etwas von jemandem will.</summary>
    public static bool IsOpen(this TaskState state) =>
        state is TaskState.Open or TaskState.InProgress or TaskState.Waiting;

    /// <summary>
    /// Ob die Aufgabe abgeschlossen ist -- erledigt oder verworfen. Beides
    /// beendet sie, nur auf verschiedene Weise.
    /// </summary>
    public static bool IsClosed(this TaskState state) => !state.IsOpen();
}
