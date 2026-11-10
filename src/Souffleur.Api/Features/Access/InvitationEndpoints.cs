using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Souffleur.Api.Infrastructure;
using Souffleur.Domain.Identity;
using Souffleur.Infrastructure.Identity;
using Souffleur.Infrastructure.Notifications;

namespace Souffleur.Api.Features.Access;

/// <summary>
/// Konten anlegen und einladen.
/// </summary>
/// <remarks>
/// Die Verwaltung vergibt kein Passwort. Sie legt das Konto an, souffleur
/// erzeugt einen Einladungslink, und die Person setzt ihr Passwort selbst.
/// So kennt niemand ausser ihr das Passwort, und es geht nie durch einen
/// Chat oder eine Mail.
///
/// Der Link ist ein Passwort-Ruecksetz-Token von ASP.NET Identity: einmal
/// benutzbar (das Setzen aendert den Sicherheitsstempel) und nach
/// <see cref="Gueltigkeit"/> verfallen. Ein neuer Link macht den alten
/// ungueltig. Ist ein Mailserver eingetragen, geht der Link per Mail hinaus;
/// sonst zeigt die Oberflaeche ihn zum Weitergeben.
/// </remarks>
public static class InvitationEndpoints
{
    /// <summary>Wie lange ein Einladungslink gilt -- in DependencyInjection eingestellt.</summary>
    public static readonly TimeSpan Gueltigkeit = TimeSpan.FromDays(7);

    public static IEndpointRouteBuilder MapInvitationEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder verwaltung = app.MapGroup("/api/access/users")
                                          .WithTags("Zugänge")
                                          .RequirePermission(Permission.AccountsManage);

        verwaltung.MapPost("/", CreateAsync);
        verwaltung.MapPost("/{id:guid}/invitation", InviteAgainAsync);
        verwaltung.MapPut("/{id:guid}/active", SetActiveAsync);

        // Ohne Anmeldung -- wer eingeladen ist, hat noch kein Passwort. Mit
        // derselben Bremse wie die Anmeldung, damit niemand Tokens durchprobiert.
        RouteGroupBuilder offen = app.MapGroup("/api/account/invitation")
                                     .WithTags("Anmeldung")
                                     .AllowAnonymous()
                                     .RequireRateLimiting("anmeldung");

        offen.MapGet("/", CheckAsync);
        offen.MapPost("/", AcceptAsync);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateUserRequest request,
        UserManager<User> users,
        AccessAdminService admin,
        IMailSender mail,
        TimeProvider time,
        HttpContext http,
        CancellationToken token)
    {
        string email = request.Email?.Trim() ?? string.Empty;
        string given = request.GivenName?.Trim() ?? string.Empty;
        string surname = request.Surname?.Trim() ?? string.Empty;

        List<string> problems = [];
        if (given.Length == 0 || surname.Length == 0)
        {
            problems.Add("Vor- und Nachname sind nötig.");
        }
        if (!email.Contains('@') || email.Length < 3)
        {
            problems.Add("Eine E-Mail-Adresse ist nötig — an sie geht die Einladung, und mit ihr meldet man sich an.");
        }
        if (problems.Count > 0)
        {
            return Problems(problems);
        }

        if (await users.FindByEmailAsync(email) is not null)
        {
            return Problems(["Mit dieser Adresse gibt es schon ein Konto."]);
        }

        var user = new User
        {
            UserName = email,
            Email = email,
            GivenName = given,
            Surname = surname,
            CreatedAt = time.GetUtcNow(),
        };

        // Ohne Passwort: Das setzt die Person selbst ueber den Link.
        IdentityResult created = await users.CreateAsync(user);
        if (!created.Succeeded)
        {
            return Problems([.. created.Errors.Select(Uebersetzt)]);
        }

        await admin.SetDirectRolesAsync(user.Id, request.RoleIds ?? [], token);

        return TypedResults.Ok(await EinladenAsync(user, users, mail, time, http, token));
    }

    /// <summary>
    /// Einen neuen Link erzeugen. Der alte gilt danach nicht mehr -- auch das
    /// ist ein Grund dafuer: ein Link, der in den falschen Chat gegangen ist.
    /// Bei einem Konto mit Passwort ist das der Weg, ein vergessenes zu ersetzen.
    /// </summary>
    private static async Task<IResult> InviteAgainAsync(
        Guid id,
        UserManager<User> users,
        IMailSender mail,
        TimeProvider time,
        HttpContext http,
        CancellationToken token)
    {
        User? user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return TypedResults.NotFound();
        }
        if (!user.IsActive)
        {
            return Problems(["Das Konto ist gesperrt. Erst freigeben, dann einladen."]);
        }

        await users.UpdateSecurityStampAsync(user);
        return TypedResults.Ok(await EinladenAsync(user, users, mail, time, http, token));
    }

    /// <summary>
    /// Sperren oder freigeben. Gesperrt heisst: keine Anmeldung, laufende
    /// Sitzungen enden mit der naechsten Pruefung des Sicherheitsstempels.
    /// Geloescht wird ein Konto nie -- Buchungen und Zeiten haengen daran.
    /// </summary>
    private static async Task<IResult> SetActiveAsync(
        Guid id,
        [FromBody] SetActiveRequest request,
        UserManager<User> users,
        AccessAdminService admin,
        HttpContext http,
        CancellationToken token)
    {
        User? user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return TypedResults.NotFound();
        }

        if (!request.IsActive && http.User.FindFirstValue(ClaimTypes.NameIdentifier) == id.ToString())
        {
            return Problems(["Das eigene Konto lässt sich nicht sperren — sonst kommt niemand mehr herein."]);
        }

        user.IsActive = request.IsActive;
        await users.UpdateAsync(user);
        if (!request.IsActive)
        {
            await users.UpdateSecurityStampAsync(user);
        }

        return TypedResults.NoContent();
    }

    /// <summary>Ob ein Link noch gilt -- und fuer wen, damit die Seite die Person begruessen kann.</summary>
    private static async Task<IResult> CheckAsync(
        [FromQuery] Guid user,
        [FromQuery] string token,
        UserManager<User> users)
    {
        User? found = await GueltigAsync(user, token, users);
        return found is null
            ? Abgelaufen()
            : TypedResults.Ok(new InvitationInfo(found.DisplayName, found.Email ?? string.Empty));
    }

    private static async Task<IResult> AcceptAsync(
        [FromBody] AcceptInvitationRequest request,
        UserManager<User> users)
    {
        User? user = await GueltigAsync(request.User, request.Token, users);
        if (user is null)
        {
            return Abgelaufen();
        }

        IdentityResult result = await users.ResetPasswordAsync(user, request.Token, request.Password ?? string.Empty);
        if (!result.Succeeded)
        {
            return result.Errors.Any(e => e.Code == "InvalidToken")
                ? Abgelaufen()
                : Problems([.. result.Errors.Select(Uebersetzt)]);
        }

        // Wer den Link aus der Mail benutzt hat, hat die Adresse damit bestaetigt.
        user.EmailConfirmed = true;
        await users.UpdateAsync(user);

        return TypedResults.NoContent();
    }

    private static async Task<User?> GueltigAsync(Guid id, string? token, UserManager<User> users)
    {
        if (string.IsNullOrEmpty(token) || await users.FindByIdAsync(id.ToString()) is not { IsActive: true } user)
        {
            return null;
        }

        bool valid = await users.VerifyUserTokenAsync(
            user, users.Options.Tokens.PasswordResetTokenProvider, UserManager<User>.ResetPasswordTokenPurpose, token);
        return valid ? user : null;
    }

    private static async Task<InvitationResponse> EinladenAsync(
        User user,
        UserManager<User> users,
        IMailSender mail,
        TimeProvider time,
        HttpContext http,
        CancellationToken token)
    {
        string secret = await users.GeneratePasswordResetTokenAsync(user);

        // Die Adresse, unter der souffleur gerade erreicht wurde -- hinter dem
        // Reverse Proxy mit dessen Namen und https (UseForwardedHeaders).
        string link = $"{http.Request.Scheme}://{http.Request.Host}{http.Request.PathBase}/einladung"
                      + $"?user={user.Id}&token={Uri.EscapeDataString(secret)}";
        DateTimeOffset until = time.GetUtcNow() + Gueltigkeit;

        bool mailed = false;
        if (mail.Mode == MailSendMode.Smtp && user.Email is { } address)
        {
            await mail.SendAsync(
                [address],
                "Einladung zu souffleur",
                $"Hallo {user.GivenName},\n\n"
                + "für dich gibt es ein Konto in souffleur. Über diesen Link legst du dein Passwort fest:\n\n"
                + $"{link}\n\n"
                + $"Der Link gilt bis {until.ToLocalTime():dd.MM.yyyy} und lässt sich einmal benutzen.\n",
                token);
            mailed = true;
        }

        return new InvitationResponse(user.Id, link, mailed, until);
    }

    /// <summary>Die Saetze von Identity sind englisch; die haeufigen auf Deutsch.</summary>
    private static string Uebersetzt(IdentityError error) => error.Code switch
    {
        "PasswordTooShort" => "Das Passwort braucht mindestens 12 Zeichen.",
        "PasswordRequiresDigit" => "Das Passwort braucht mindestens eine Ziffer.",
        "PasswordRequiresUpper" => "Das Passwort braucht mindestens einen Großbuchstaben.",
        "PasswordRequiresLower" => "Das Passwort braucht mindestens einen Kleinbuchstaben.",
        "PasswordRequiresUniqueChars" => "Das Passwort braucht mehr verschiedene Zeichen.",
        "DuplicateEmail" or "DuplicateUserName" => "Mit dieser Adresse gibt es schon ein Konto.",
        "InvalidEmail" => "Die E-Mail-Adresse ist ungültig.",
        _ => error.Description,
    };

    private static IResult Problems(IEnumerable<string> problems) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["Konto"] = [.. problems] });

    private static IResult Abgelaufen() =>
        TypedResults.Problem(
            "Der Link ist abgelaufen oder schon benutzt. Bitte um einen neuen.",
            statusCode: StatusCodes.Status410Gone);
}

public record CreateUserRequest(string? GivenName, string? Surname, string? Email, Guid[]? RoleIds);

public record SetActiveRequest(bool IsActive);

public record AcceptInvitationRequest(Guid User, string Token, string? Password);

public record InvitationInfo(string Name, string Email);

public record InvitationResponse(Guid UserId, string Link, bool Mailed, DateTimeOffset ValidUntil);
