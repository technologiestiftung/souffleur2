using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Souffleur.Domain.Identity;
using Souffleur.Infrastructure.Identity;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Api.Features.Authentication;

/// <summary>
/// An- und Abmeldung. Es gibt bewusst keine Selbstregistrierung: Konten legt die
/// Verwaltung des Hauses an, weil jedes Konto Personal- und Finanzdaten sieht.
/// </summary>
public static class AuthenticationEndpoints
{
    public static IEndpointRouteBuilder MapAuthenticationEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/account").WithTags("Account");

        group.MapPost("/login", LoginAsync).AllowAnonymous().RequireRateLimiting("anmeldung");
        group.MapPost("/logout", LogoutAsync).RequireAuthorization();
        group.MapGet("/me", GetCurrentAsync).RequireAuthorization();
        group.MapPost("/password", ChangePasswordAsync).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> LoginAsync(
        [FromBody] LoginRequest request,
        SignInManager<User> signInManager,
        UserManager<User> userManager,
        TimeProvider timeProvider,
        AccessService access,
        IServiceProvider services)
    {
        User? user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive)
        {
            // Dieselbe Antwort wie bei falschem Passwort -- sonst verraet die API,
            // welche Adressen ein Konto haben.
            return TypedResults.Problem("Anmeldung fehlgeschlagen.", statusCode: StatusCodes.Status401Unauthorized);
        }

        Microsoft.AspNetCore.Identity.SignInResult result = await signInManager.PasswordSignInAsync(
            user, request.Password, isPersistent: request.RememberMe, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            return TypedResults.Problem(
                "Das Konto ist voruebergehend gesperrt. Bitte spaeter erneut versuchen.",
                statusCode: StatusCodes.Status423Locked);
        }

        if (!result.Succeeded)
        {
            return TypedResults.Problem("Anmeldung fehlgeschlagen.", statusCode: StatusCodes.Status401Unauthorized);
        }

        user.LastLoginAt = timeProvider.GetUtcNow();
        await userManager.UpdateAsync(user);

        // Nur auf der Vorfuehrinstanz: Konten, die an ein Haus gebunden sind,
        // bringen ihr Haus mit. Ohne den Schalter geschieht hier nichts.
        await DemoHouses.SwitchForAccountAsync(services, user.Email ?? string.Empty);

        return TypedResults.Ok(CurrentUserResponse.From(user, await userManager.GetRolesAsync(user), await access.PermissionsOfAsync(user.Id)));
    }

    private static async Task<IResult> LogoutAsync(SignInManager<User> signInManager)
    {
        await signInManager.SignOutAsync();
        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetCurrentAsync(
        UserManager<User> userManager,
        HttpContext context,
        AccessService access)
    {
        User? user = await userManager.GetUserAsync(context.User);
        return user is null
            ? TypedResults.Unauthorized()
            : TypedResults.Ok(CurrentUserResponse.From(user, await userManager.GetRolesAsync(user), await access.PermissionsOfAsync(user.Id)));
    }

    private static async Task<IResult> ChangePasswordAsync(
        [FromBody] ChangePasswordRequest request,
        UserManager<User> userManager,
        HttpContext context)
    {
        User? user = await userManager.GetUserAsync(context.User);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        IdentityResult result = await userManager.ChangePasswordAsync(
            user, request.CurrentPassword, request.NewPassword);

        return result.Succeeded
            ? TypedResults.NoContent()
            : TypedResults.ValidationProblem(
                result.Errors.GroupBy(e => e.Code).ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));
    }
}

public record LoginRequest(string Email, string Password, bool RememberMe = false);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <remarks>
/// Die Rechte als Namen, nicht als Zahlen: Die Oberflaeche blendet damit aus,
/// was jemand ohnehin nicht darf -- etwa die Verwaltung der Zugaenge. Das ist
/// Bequemlichkeit, keine Sicherung; die Schnittstelle prueft selbst.
/// </remarks>
public record CurrentUserResponse(
    Guid Id,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions)
{
    public static CurrentUserResponse From(
        User user,
        IEnumerable<string> roles,
        IEnumerable<Permission> permissions) =>
        new(user.Id, user.Email ?? string.Empty, user.DisplayName, [.. roles],
            [.. permissions.Order().Select(p => p.ToString())]);
}
