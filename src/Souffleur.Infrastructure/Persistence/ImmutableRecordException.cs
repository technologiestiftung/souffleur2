using Microsoft.EntityFrameworkCore;

namespace Souffleur.Infrastructure.Persistence;

/// <summary>
/// Der Versuch, einen festgeschriebenen Datensatz zu aendern oder zu loeschen.
/// Korrigiert wird ausschliesslich per Storno.
/// </summary>
public class ImmutableRecordException(string entityName, DateTimeOffset committedAt, EntityState attemptedState)
    : InvalidOperationException(
        $"{entityName} wurde am {committedAt:yyyy-MM-dd HH:mm} festgeschrieben und laesst " +
        $"sich nicht mehr aendern (Versuch: {attemptedState}). Stattdessen stornieren.")
{
    public string EntityName { get; } = entityName;

    public DateTimeOffset CommittedAt { get; } = committedAt;

    public EntityState AttemptedState { get; } = attemptedState;
}
