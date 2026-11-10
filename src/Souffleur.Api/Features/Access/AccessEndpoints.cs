using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Souffleur.Api.Infrastructure;
using Souffleur.Domain.Identity;
using Souffleur.Infrastructure.Identity;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Api.Features.Access;

/// <summary>
/// Rollen, Gruppen und wer wozu gehoert. Alles hier verlangt dasselbe Recht:
/// Wer Rechte vergeben darf, darf auch sehen, wer welche hat.
/// </summary>
public static class AccessEndpoints
{
    private const int HoechstlaengeName = 100;

    public static IEndpointRouteBuilder MapAccessEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/access")
                                     .WithTags("Zugänge")
                                     .RequirePermission(Permission.AccountsManage);

        group.MapGet("/permissions", Katalog);

        group.MapGet("/roles", RolesAsync);
        group.MapPost("/roles", CreateRoleAsync);
        group.MapPut("/roles/{id:guid}", UpdateRoleAsync);
        group.MapDelete("/roles/{id:guid}", DeleteRoleAsync);

        group.MapGet("/groups", GroupsAsync);
        group.MapPost("/groups", CreateGroupAsync);
        group.MapPut("/groups/{id:guid}", UpdateGroupAsync);
        group.MapDelete("/groups/{id:guid}", DeleteGroupAsync);

        group.MapGet("/users", UsersAsync);
        group.MapPut("/users/{id:guid}/roles", SetUserRolesAsync);

        // Die Gruppen zum Auswaehlen -- eine Aufgabe an „die Technik" geben
        // darf jeder, der Aufgaben anlegt. Ohne Mitglieder und Rollen: Wer
        // wozu gehoert, ist Sache der Verwaltung.
        RouteGroupBuilder offen = app.MapGroup("/api/groups")
                                     .WithTags("Zugänge")
                                     .RequireAuthorization();
        offen.MapGet("/", ActiveGroupsAsync);
        offen.MapGet("/mine", MyGroupsAsync);
        offen.MapGet("/{id:guid}/contacts", ContactsOfGroupAsync);

        return app;
    }

    private static async Task<IResult> ActiveGroupsAsync(AccessAdminService admin) =>
        TypedResults.Ok((await admin.GroupsAsync())
            .Where(g => g.IsActive)
            .Select(g => new GroupSummary(g.Id, g.Name, g.Note)));

    /// <summary>Welche Kontakte zu einer Gruppe gehoeren -- nur die Kennungen.</summary>
    private static async Task<IResult> ContactsOfGroupAsync(Guid id, AccessService access) =>
        TypedResults.Ok(await access.ContactsOfGroupAsync(id));

    private static async Task<IResult> MyGroupsAsync(
        AccessService access,
        Souffleur.Infrastructure.Persistence.ICurrentUser currentUser)
    {
        if (currentUser.Id is not { } userId)
        {
            return TypedResults.Ok(Array.Empty<GroupSummary>());
        }

        return TypedResults.Ok((await access.GroupsOfAsync(userId))
            .Select(g => new GroupSummary(g.Id, g.Name, g.Note)));
    }

    private static IResult Katalog() =>
        TypedResults.Ok(Permissions.All.Select(p => new PermissionResponse(
            p, p.ToString(), p.Label(), p.Area())));

    // ── Rollen ───────────────────────────────────────────────────────────

    private static async Task<IResult> RolesAsync(AccessAdminService admin) =>
        TypedResults.Ok((await admin.RolesAsync()).Select(ToResponse));

    private static async Task<IResult> CreateRoleAsync(
        [FromBody] RoleRequest request,
        AccessAdminService admin)
    {
        if (Pruefen(request.Name) is { } problem)
        {
            return problem;
        }

        return await Eindeutig(async () =>
        {
            Role role = await admin.CreateRoleAsync(
                request.Name.Trim(), Leer(request.Description), request.Permissions ?? []);
            return TypedResults.Created($"/api/access/roles/{role.Id}", new { role.Id });
        }, "Eine Rolle mit diesem Namen gibt es schon.");
    }

    private static async Task<IResult> UpdateRoleAsync(
        Guid id,
        [FromBody] RoleRequest request,
        AccessAdminService admin)
    {
        if (Pruefen(request.Name) is { } problem)
        {
            return problem;
        }

        return await Eindeutig(async () =>
            await admin.UpdateRoleAsync(id, request.Name.Trim(), Leer(request.Description), request.Permissions ?? [])
                ? TypedResults.NoContent()
                : TypedResults.NotFound(),
            "Eine Rolle mit diesem Namen gibt es schon.");
    }

    private static async Task<IResult> DeleteRoleAsync(Guid id, AccessAdminService admin) =>
        await admin.DeleteRoleAsync(id)
            ? TypedResults.NoContent()
            : TypedResults.Problem(
                "Diese Rolle gibt es nicht, oder sie gehört zu den mitgelieferten und bleibt.",
                statusCode: StatusCodes.Status409Conflict);

    // ── Gruppen ──────────────────────────────────────────────────────────

    private static async Task<IResult> GroupsAsync(AccessAdminService admin) =>
        TypedResults.Ok((await admin.GroupsAsync()).Select(g => new GroupResponse(
            g.Id,
            g.Name,
            g.Note,
            g.IsActive,
            [.. g.Roles.Select(r => r.RoleId)],
            [.. g.Members.Select(m => m.UserId)])));

    private static async Task<IResult> CreateGroupAsync(
        [FromBody] GroupRequest request,
        AccessAdminService admin)
    {
        if (Pruefen(request.Name) is { } problem)
        {
            return problem;
        }

        return await Eindeutig(async () =>
        {
            var group = await admin.CreateGroupAsync(
                request.Name.Trim(), Leer(request.Note), request.RoleIds ?? [], request.MemberIds ?? []);
            return TypedResults.Created($"/api/access/groups/{group.Id}", new { group.Id });
        }, "Eine Gruppe mit diesem Namen gibt es schon.");
    }

    private static async Task<IResult> UpdateGroupAsync(
        Guid id,
        [FromBody] GroupRequest request,
        AccessAdminService admin)
    {
        if (Pruefen(request.Name) is { } problem)
        {
            return problem;
        }

        return await Eindeutig(async () =>
            await admin.UpdateGroupAsync(
                id, request.Name.Trim(), Leer(request.Note), request.IsActive ?? true,
                request.RoleIds ?? [], request.MemberIds ?? [])
                ? TypedResults.NoContent()
                : TypedResults.NotFound(),
            "Eine Gruppe mit diesem Namen gibt es schon.");
    }

    private static async Task<IResult> DeleteGroupAsync(Guid id, AccessAdminService admin) =>
        await admin.DeleteGroupAsync(id) ? TypedResults.NoContent() : TypedResults.NotFound();

    // ── Konten ───────────────────────────────────────────────────────────

    private static async Task<IResult> UsersAsync(
        AccessAdminService admin,
        SouffleurDbContext context,
        IConfiguration configuration,
        HttpContext http)
    {
        // Nur auf der Vorfuehrinstanz: Konten anderer Haeuser bleiben verborgen.
        HashSet<Guid> verborgen = await DemoHouses.HiddenAccountsAsync(
            context, configuration, http.User.Identity?.Name);

        var antworten = new List<UserResponse>();
        foreach (var user in (await admin.UsersAsync()).Where(u => !verborgen.Contains(u.Id)))
        {
            antworten.Add(new UserResponse(
                user.Id, user.DisplayName, user.Email, user.IsActive,
                [.. await admin.DirectRolesAsync(user.Id)],
                HasPassword: user.PasswordHash is not null,
                user.LastLoginAt));
        }

        return TypedResults.Ok(antworten);
    }

    private static async Task<IResult> SetUserRolesAsync(
        Guid id,
        [FromBody] UserRolesRequest request,
        AccessAdminService admin) =>
        await admin.SetDirectRolesAsync(id, request.RoleIds ?? [])
            ? TypedResults.NoContent()
            : TypedResults.NotFound();

    // ── Hilfen ───────────────────────────────────────────────────────────

    private static RoleResponse ToResponse(Role role) => new(
        role.Id,
        role.Name ?? string.Empty,
        role.IsBuiltIn ? Roles.Label(role.Name) : role.Name ?? string.Empty,
        role.Description,
        role.IsBuiltIn,
        [.. role.Permissions.Select(p => p.Permission).Order()]);

    private static IResult? Pruefen(string? name) =>
        string.IsNullOrWhiteSpace(name) || name.Trim().Length > HoechstlaengeName
            ? TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = [$"Ein Name ist nötig, höchstens {HoechstlaengeName} Zeichen."],
            })
            : null;

    private static string? Leer(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>
    /// Der eindeutige Index ist die eigentliche Pruefung -- ein Vorab-Blick in
    /// die Tabelle liesse zwischen Blick und Schreiben eine Luecke.
    /// </summary>
    private static async Task<IResult> Eindeutig(Func<Task<IResult>> aktion, string meldung)
    {
        try
        {
            return await aktion();
        }
        catch (DbUpdateException)
        {
            return TypedResults.Problem(meldung, statusCode: StatusCodes.Status409Conflict);
        }
    }
}

public record PermissionResponse(Permission Permission, string Key, string Label, string Area);

public record RoleRequest(string Name, string? Description, Permission[]? Permissions);

/// <param name="Label">Was die Oberflaeche zeigt -- bei mitgelieferten Rollen deutsch.</param>
public record RoleResponse(
    Guid Id, string Name, string Label, string? Description, bool IsBuiltIn, Permission[] Permissions);

public record GroupRequest(
    string Name, string? Note, bool? IsActive, Guid[]? RoleIds, Guid[]? MemberIds);

public record GroupResponse(
    Guid Id, string Name, string? Note, bool IsActive, Guid[] RoleIds, Guid[] MemberIds);

/// <param name="HasPassword">Ohne Passwort ist jemand eingeladen, hat den Link aber noch nicht benutzt.</param>
public record UserResponse(
    Guid Id, string Name, string? Email, bool IsActive, Guid[] RoleIds,
    bool HasPassword = true, DateTimeOffset? LastLoginAt = null);

public record UserRolesRequest(Guid[]? RoleIds);

public record GroupSummary(Guid Id, string Name, string? Note);
