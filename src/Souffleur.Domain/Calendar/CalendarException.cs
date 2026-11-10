namespace Souffleur.Domain.Calendar;

/// <summary>
/// Ein Termin oder eine Serie wurde nicht angenommen. Trägt alle Gründe, nicht
/// nur den ersten -- wer ein Formular abschickt, will nicht dreimal nachbessern.
/// </summary>
public class CalendarException(IReadOnlyList<string> problems)
    : Exception(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;

    public CalendarException(string problem) : this([problem])
    {
    }
}
