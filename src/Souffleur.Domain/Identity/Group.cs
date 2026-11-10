using Souffleur.Domain.Common;

namespace Souffleur.Domain.Identity;

/// <summary>
/// Eine Gruppe von Leuten im Haus -- „Technik", „Abenddienst", „Werkstatt".
/// </summary>
/// <remarks>
/// Gruppen tun vier Dinge auf einmal, und das ist Absicht: Sie buendeln
/// Rechte, steuern die Sichtbarkeit von Dokumenten, nehmen Zuweisungen
/// entgegen und engen die Auswahl beim Einteilen ein. Ein Haus denkt in
/// „die Technik", nicht in vier getrennten Listen derselben Leute.
/// </remarks>
public class Group : Entity
{
    public required string Name { get; set; }

    public string? Note { get; set; }

    /// <summary>
    /// Aus dem Verkehr gezogen statt geloescht: An einer Gruppe koennen
    /// Zuweisungen und Dokumente haengen, die ihren Sinn verloeren.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public List<GroupMember> Members { get; set; } = [];

    public List<GroupRole> Roles { get; set; } = [];
}

/// <summary>Wer zu einer Gruppe gehoert.</summary>
public class GroupMember : Entity
{
    public required Guid GroupId { get; set; }

    public required Guid UserId { get; set; }
}

/// <summary>
/// Welche Rolle eine Gruppe traegt. Wer in der Gruppe ist, hat deren Rechte.
/// </summary>
public class GroupRole : Entity
{
    public required Guid GroupId { get; set; }

    public required Guid RoleId { get; set; }
}

/// <summary>Ein Recht an einer Rolle.</summary>
public class RolePermission : Entity
{
    public required Guid RoleId { get; set; }

    public required Permission Permission { get; set; }
}
