using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Souffleur.Domain.Identity;

namespace Souffleur.Infrastructure.Identity;

/// <summary>
/// Legt die Rechte einer Person beim Anmelden in ihre Sitzung.
/// </summary>
/// <remarks>
/// Warum nicht bei jeder Anfrage nachsehen: Jeder Aufruf wuerde dann zwei
/// zusaetzliche Abfragen kosten, und ein Haus auf einer Synology hat nicht
/// viel zu verschenken.
///
/// Der Preis ist, dass eine Rechteaenderung nicht sofort greift. Darum steht
/// das Pruefintervall der Anmeldung auf zwei Minuten statt auf den
/// vorgegebenen dreissig: Wer jemandem ein Recht nimmt, will nicht eine halbe
/// Stunde warten -- und wer eines bekommt, soll nicht so lange vor einer
/// gesperrten Seite sitzen. Wer Rechte aendert, stoesst ausserdem die
/// Erneuerung bei allen Betroffenen an.
/// </remarks>
public class PermissionClaimsFactory(
    UserManager<User> userManager,
    RoleManager<Role> roleManager,
    IOptions<IdentityOptions> options,
    AccessService access)
    : UserClaimsPrincipalFactory<User, Role>(userManager, roleManager, options)
{
    /// <summary>Der Anspruch, unter dem ein einzelnes Recht in der Sitzung steht.</summary>
    public const string ClaimType = "souffleur:recht";

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(User user)
    {
        ClaimsIdentity identity = await base.GenerateClaimsAsync(user);

        foreach (Permission permission in await access.PermissionsOfAsync(user.Id))
        {
            // Die Zahl, nicht der Name: Sie steht so auch in der Datenbank,
            // und ein umbenanntes Recht soll keine Sitzung entwerten.
            identity.AddClaim(new Claim(ClaimType, ((int)permission).ToString()));
        }

        return identity;
    }
}
