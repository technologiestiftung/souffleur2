using System.Security.Claims;
using Souffleur.Domain.Identity;
using Souffleur.Infrastructure.Identity;

namespace Souffleur.Api.Infrastructure;

/// <summary>
/// Bindet Endpunkte an Rechte statt an Rollennamen.
/// </summary>
/// <remarks>
/// Steht hier und nicht bei der Claims-Fabrik: Die Erweiterung haengt am
/// Routing, und das kennt nur das API-Projekt.
///
/// Der Unterschied zu vorher ist nicht nur technisch. <c>RequireRole(
/// Administrator, Management, Accounting)</c> zaehlte auf, WER darf; hier
/// steht, WOFUER das Recht gilt. Wer eine neue Rolle anlegt, muss nicht mehr
/// durch den Quelltext und sich in Aufzaehlungen eintragen.
/// </remarks>
public static class PermissionPolicies
{
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, Permission permission)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(policy =>
            policy.RequireClaim(PermissionClaimsFactory.ClaimType, ((int)permission).ToString()));

    /// <summary>Wer eines der Rechte hat, kommt durch.</summary>
    public static TBuilder RequireAnyPermission<TBuilder>(
        this TBuilder builder,
        params Permission[] permissions)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(policy =>
            policy.RequireClaim(
                PermissionClaimsFactory.ClaimType,
                permissions.Select(p => ((int)p).ToString())));

    /// <summary>
    /// Ob die Sitzung ein Recht traegt -- fuer Endpunkte, die allen offenstehen,
    /// aber je nach Recht mehr oder weniger zeigen.
    /// </summary>
    public static bool Has(this ClaimsPrincipal principal, Permission permission) =>
        principal.HasClaim(PermissionClaimsFactory.ClaimType, ((int)permission).ToString());
}
