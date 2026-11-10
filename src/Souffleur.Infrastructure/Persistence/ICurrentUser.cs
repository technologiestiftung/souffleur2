namespace Souffleur.Infrastructure.Persistence;

/// <summary>
/// Wer gerade handelt. Ohne Anmeldung -- Hintergrunddienste, Migrationen --
/// ist <see cref="Id"/> <c>null</c>.
/// </summary>
public interface ICurrentUser
{
    Guid? Id { get; }
}
