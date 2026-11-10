using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Identity;
using Souffleur.Infrastructure.Conversations;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Identity;

/// <summary>
/// Rollen, Gruppen und wer wozu gehoert.
/// </summary>
/// <remarks>
/// Getrennt vom <see cref="AccessService"/>: Der wird bei jeder Anmeldung
/// gebraucht und soll schlank bleiben. Hier liegt, was selten passiert.
///
/// Jede Aenderung an Rechten erneuert die Anmeldung der Betroffenen. Ohne das
/// behielte jemand bis zu seiner naechsten Anmeldung Rechte, die ihm gerade
/// genommen wurden -- und genommen werden sie meistens aus einem Grund.
/// </remarks>
public class AccessAdminService(
    SouffleurDbContext context,
    UserManager<User> users,
    AccessService access)
{
    // ── Rollen ───────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<Role>> RolesAsync(CancellationToken cancellationToken = default) =>
        await context.Roles
            .Include(r => r.Permissions)
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);

    public async Task<Role> CreateRoleAsync(
        string name,
        string? description,
        IReadOnlyCollection<Permission> permissions,
        CancellationToken cancellationToken = default)
    {
        var role = new Role
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            Description = description,
            IsBuiltIn = false,
        };

        context.Roles.Add(role);
        context.RolePermissions.AddRange(permissions.Distinct().Select(p => new RolePermission
        {
            RoleId = role.Id,
            Permission = p,
        }));

        await context.SaveChangesAsync(cancellationToken);
        return role;
    }

    /// <summary>
    /// Benennt um und setzt die Rechte neu. Auch eine mitgelieferte Rolle
    /// laesst sich aendern -- nur loeschen nicht.
    /// </summary>
    public async Task<bool> UpdateRoleAsync(
        Guid id,
        string name,
        string? description,
        IReadOnlyCollection<Permission> permissions,
        CancellationToken cancellationToken = default)
    {
        Role? role = await context.Roles.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (role is null)
        {
            return false;
        }

        // Der Name einer mitgelieferten Rolle ist ihr Schluessel -- siehe
        // Roles.Label. Umbenennen hiesse, beim naechsten Start eine zweite
        // anzulegen.
        if (!role.IsBuiltIn)
        {
            role.Name = name;
            role.NormalizedName = name.ToUpperInvariant();
        }

        role.Description = description;

        // In zwei Zuegen: Alte loeschen und neue einfuegen in einem Rutsch
        // laesst EF ueber dieselben Schluessel stolpern, wenn ein Recht
        // bleibt. Erst raeumen, dann fuellen.
        await context.RolePermissions
            .Where(p => p.RoleId == id)
            .ExecuteDeleteAsync(cancellationToken);

        context.RolePermissions.AddRange(permissions.Distinct().Select(p => new RolePermission
        {
            RoleId = id,
            Permission = p,
        }));

        await context.SaveChangesAsync(cancellationToken);
        await RefreshAffectedAsync(id, cancellationToken);
        return true;
    }

    /// <summary>
    /// Loescht eine Rolle. Mitgelieferte bleiben -- ein Haus ohne
    /// Administrator koennte sich selbst nicht mehr aufschliessen.
    /// </summary>
    public async Task<bool> DeleteRoleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Role? role = await context.Roles.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (role is null || role.IsBuiltIn)
        {
            return false;
        }

        IReadOnlyList<Guid> betroffen = await access.AffectedByRoleAsync(id, cancellationToken);

        context.Roles.Remove(role);
        await context.SaveChangesAsync(cancellationToken);
        await RefreshAsync(betroffen, cancellationToken);
        return true;
    }

    // ── Gruppen ──────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<Group>> GroupsAsync(CancellationToken cancellationToken = default) =>
        await context.Groups
            .Include(g => g.Members)
            .Include(g => g.Roles)
            .OrderBy(g => g.Name)
            .ToListAsync(cancellationToken);

    public async Task<Group> CreateGroupAsync(
        string name,
        string? note,
        IReadOnlyCollection<Guid> roleIds,
        IReadOnlyCollection<Guid> memberIds,
        CancellationToken cancellationToken = default)
    {
        var group = new Group { Name = name, Note = note };
        context.Groups.Add(group);
        Fuellen(group.Id, roleIds, memberIds);

        await context.SaveChangesAsync(cancellationToken);
        await RefreshAsync(memberIds, cancellationToken);
        return group;
    }

    public async Task<bool> UpdateGroupAsync(
        Guid id,
        string name,
        string? note,
        bool isActive,
        IReadOnlyCollection<Guid> roleIds,
        IReadOnlyCollection<Guid> memberIds,
        CancellationToken cancellationToken = default)
    {
        Group? group = await context.Groups.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (group is null)
        {
            return false;
        }

        // Wer vorher drin war, muss auch erneuert werden -- sonst behaelt
        // jemand, den man gerade herausgenommen hat, seine Rechte.
        List<Guid> vorher = await context.GroupMembers
            .Where(m => m.GroupId == id)
            .Select(m => m.UserId)
            .ToListAsync(cancellationToken);

        group.Name = name;
        group.Note = note;
        group.IsActive = isActive;

        await context.GroupMembers.Where(m => m.GroupId == id).ExecuteDeleteAsync(cancellationToken);
        await context.GroupRoles.Where(r => r.GroupId == id).ExecuteDeleteAsync(cancellationToken);
        Fuellen(id, roleIds, memberIds);

        await context.SaveChangesAsync(cancellationToken);
        await RefreshAsync([.. vorher.Concat(memberIds).Distinct()], cancellationToken);

        // Die Kanaele der Gruppe ziehen mit: Wer neu dabei ist, ist im Kanal.
        await GroupChannels.SyncAsync(context, id, DateTimeOffset.UtcNow, cancellationToken);
        return true;
    }

    public async Task<bool> DeleteGroupAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Group? group = await context.Groups.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (group is null)
        {
            return false;
        }

        List<Guid> mitglieder = await context.GroupMembers
            .Where(m => m.GroupId == id)
            .Select(m => m.UserId)
            .ToListAsync(cancellationToken);

        context.Groups.Remove(group);

        // Der Kanal der Gruppe bleibt mit den Leuten, die zuletzt drin waren --
        // er folgt nur keiner Gruppe mehr.
        await context.Conversations.Where(c => c.GroupId == id)
            .ExecuteUpdateAsync(c => c.SetProperty(x => x.GroupId, (Guid?)null), cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        await RefreshAsync(mitglieder, cancellationToken);
        return true;
    }

    private void Fuellen(
        Guid groupId,
        IReadOnlyCollection<Guid> roleIds,
        IReadOnlyCollection<Guid> memberIds)
    {
        context.GroupRoles.AddRange(roleIds.Distinct().Select(roleId => new GroupRole
        {
            GroupId = groupId,
            RoleId = roleId,
        }));

        context.GroupMembers.AddRange(memberIds.Distinct().Select(userId => new GroupMember
        {
            GroupId = groupId,
            UserId = userId,
        }));
    }

    // ── Konten ───────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<User>> UsersAsync(CancellationToken cancellationToken = default) =>
        await context.Users
            .OrderBy(u => u.Surname)
            .ThenBy(u => u.GivenName)
            .ToListAsync(cancellationToken);

    /// <summary>Die Rollen, die jemand unmittelbar traegt -- ohne die aus Gruppen.</summary>
    public async Task<IReadOnlyList<Guid>> DirectRolesAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await context.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync(cancellationToken);

    public async Task<bool> SetDirectRolesAsync(
        Guid userId,
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken = default)
    {
        User? user = await context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return false;
        }

        await context.UserRoles.Where(ur => ur.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        context.UserRoles.AddRange(roleIds.Distinct().Select(roleId => new IdentityUserRole<Guid>
        {
            UserId = userId,
            RoleId = roleId,
        }));

        await context.SaveChangesAsync(cancellationToken);
        await RefreshAsync([userId], cancellationToken);
        return true;
    }

    // ── Anmeldungen erneuern ─────────────────────────────────────────────

    private async Task RefreshAffectedAsync(Guid roleId, CancellationToken cancellationToken) =>
        await RefreshAsync(await access.AffectedByRoleAsync(roleId, cancellationToken), cancellationToken);

    /// <summary>
    /// Stoesst an, dass die genannten Anmeldungen ihre Rechte neu holen.
    /// </summary>
    /// <remarks>
    /// Der Sicherheitsstempel ist das, woran ASP.NET Identity erkennt, dass
    /// eine Sitzung veraltet ist. Beim naechsten Pruefintervall -- zwei
    /// Minuten -- wird das Cookie neu ausgestellt, mitsamt den jetzt
    /// geltenden Rechten.
    /// </remarks>
    private async Task RefreshAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken)
    {
        foreach (Guid userId in userIds.Distinct())
        {
            User? user = await users.FindByIdAsync(userId.ToString());
            if (user is not null)
            {
                await users.UpdateSecurityStampAsync(user);
            }
        }
    }
}
