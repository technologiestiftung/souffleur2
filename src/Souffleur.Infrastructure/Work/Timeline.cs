using Souffleur.Domain.Work;

namespace Souffleur.Infrastructure.Work;

/// <summary>
/// Eine Reihenfolge, die nicht mehr aufgeht.
/// </summary>
public sealed record DependencyBreach(
    Guid TaskId, string TaskTitle, Guid DependsOnId, string DependsOnTitle);

/// <summary>
/// Was der Zeitstrahl zeigt.
/// </summary>
/// <remarks>
/// Eine andere Frage als das Jahresraster des Kalenders (ADR-0006): Dort geht
/// es darum, was an einem Tag los ist, hier darum, wie lange etwas dauert und
/// was daran hängt. Deshalb eine eigene Ansicht und keine zweite Spielart der
/// vorhandenen.
/// </remarks>
/// <param name="Unscheduled">
/// Wie viele Aufgaben weder Anfang noch Frist haben. Sie erscheinen hier
/// nicht -- aber verschwiegen werden sie auch nicht.
/// </param>
public sealed record Timeline(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<TaskItem> Tasks,
    IReadOnlyList<TaskDependency> Dependencies,
    IReadOnlyList<DependencyBreach> Breaches,
    int Unscheduled);
