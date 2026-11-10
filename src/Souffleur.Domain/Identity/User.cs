using Microsoft.AspNetCore.Identity;

namespace Souffleur.Domain.Identity;

/// <summary>
/// Eine Person, die mit souffleur arbeitet. Anmeldung, Passwort und Sperre
/// verwaltet ASP.NET Identity.
/// </summary>
public class User : IdentityUser<Guid>
{
    public required string GivenName { get; set; }

    public required string Surname { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Letzte erfolgreiche Anmeldung; fuer die Frage, wer ein Konto noch braucht.</summary>
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>
    /// Deaktivierte Konten bleiben erhalten, weil sie an Buchungen und Zeiteintraegen
    /// haengen; geloescht wird nicht.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public string DisplayName => $"{GivenName} {Surname}";
}
