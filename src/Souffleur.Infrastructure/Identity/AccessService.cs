using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Identity;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Identity;

/// <summary>
/// Wer was darf -- Rollen, Gruppen und die Rechte, die daraus folgen.
/// </summary>
public class AccessService(SouffleurDbContext context)
{
    /// <summary>
    /// Alle Rechte einer Person: aus ihren eigenen Rollen und aus den Rollen
    /// der Gruppen, in denen sie ist.
    /// </summary>
    /// <remarks>
    /// Rechte addieren sich und werden nie entzogen. Ein Verbot, das ein
    /// Recht aus einer anderen Rolle aussticht, waere beim Nachsehen nicht
    /// mehr zu beantworten: „Warum darf sie das nicht?" haette dann keine
    /// Antwort, sondern eine Rangfolge.
    /// </remarks>
    public async Task<IReadOnlySet<Permission>> PermissionsOfAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Die eigenen Rollen und die aus den Gruppen in einem Zug.
        IQueryable<Guid> eigene = context.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId);

        IQueryable<Guid> ueberGruppen = context.GroupMembers
            .Where(m => m.UserId == userId)
            .Join(context.Groups.Where(g => g.IsActive),
                  m => m.GroupId,
                  g => g.Id,
                  (m, g) => g.Id)
            .Join(context.GroupRoles, id => id, gr => gr.GroupId, (_, gr) => gr.RoleId);

        List<Permission> rechte = await context.RolePermissions
            .Where(p => eigene.Contains(p.RoleId) || ueberGruppen.Contains(p.RoleId))
            .Select(p => p.Permission)
            .Distinct()
            .ToListAsync(cancellationToken);

        return rechte.ToHashSet();
    }

    /// <summary>
    /// Wer ein Recht hat -- ueber eigene Rollen oder ueber Gruppen. Fuer die
    /// Glocke: Ein Urlaubsantrag geht an alle, die ihn entscheiden koennen.
    /// </summary>
    public async Task<List<Guid>> UsersWithPermissionAsync(
        Permission permission,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Guid> rollen = context.RolePermissions
            .Where(p => p.Permission == permission)
            .Select(p => p.RoleId);

        List<Guid> direkt = await context.UserRoles
            .Where(ur => rollen.Contains(ur.RoleId))
            .Select(ur => ur.UserId)
            .ToListAsync(cancellationToken);

        List<Guid> ueberGruppen = await context.GroupRoles
            .Where(gr => rollen.Contains(gr.RoleId))
            .Join(context.Groups.Where(g => g.IsActive), gr => gr.GroupId, g => g.Id, (gr, _) => gr.GroupId)
            .Join(context.GroupMembers, id => id, m => m.GroupId, (_, m) => m.UserId)
            .ToListAsync(cancellationToken);

        return [.. direkt.Concat(ueberGruppen).Distinct()];
    }

    /// <summary>Die Gruppen, in denen jemand ist.</summary>
    public async Task<IReadOnlyList<Group>> GroupsOfAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await context.GroupMembers
            .Where(m => m.UserId == userId)
            .Join(context.Groups, m => m.GroupId, g => g.Id, (_, g) => g)
            .Where(g => g.IsActive)
            .OrderBy(g => g.Name)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Die Kontakte hinter den Konten einer Gruppe -- fuer den Dienstplan,
    /// der Kontakte einteilt und nicht Konten.
    /// </summary>
    /// <remarks>
    /// Wer kein Konto hat oder dessen Konto mit keinem Kontakt verbunden ist,
    /// faellt hier heraus. Das ist richtig so: Eine Gruppe besteht aus Leuten,
    /// die souffleur benutzen. Die freie Beleuchterin ohne Konto steht beim
    /// Besetzen trotzdem zur Wahl -- unter „alle".
    /// </remarks>
    public async Task<IReadOnlyList<Guid>> ContactsOfGroupAsync(
        Guid groupId,
        CancellationToken cancellationToken = default) =>
        await context.GroupMembers
            .Where(m => m.GroupId == groupId)
            .Join(context.Contacts.Where(c => c.UserId != null && c.IsActive),
                  m => m.UserId,
                  c => c.UserId!.Value,
                  (_, c) => c.Id)
            .Distinct()
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Wessen Rechte sich geaendert haben, wenn an dieser Rolle etwas
    /// geaendert wurde -- direkt oder ueber eine Gruppe.
    /// </summary>
    /// <remarks>
    /// Gebraucht, um die betroffenen Anmeldungen zu erneuern. Ohne das
    /// behielte jemand bis zu seiner naechsten Anmeldung Rechte, die ihm
    /// gerade genommen wurden.
    /// </remarks>
    public async Task<IReadOnlyList<Guid>> AffectedByRoleAsync(
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        List<Guid> direkt = await context.UserRoles
            .Where(ur => ur.RoleId == roleId)
            .Select(ur => ur.UserId)
            .ToListAsync(cancellationToken);

        List<Guid> ueberGruppen = await context.GroupRoles
            .Where(gr => gr.RoleId == roleId)
            .Join(context.GroupMembers, gr => gr.GroupId, m => m.GroupId, (_, m) => m.UserId)
            .ToListAsync(cancellationToken);

        return [.. direkt.Concat(ueberGruppen).Distinct()];
    }
}
