using Souffleur.Domain.Identity;
using Souffleur.Infrastructure.Identity;
using Souffleur.Api.Infrastructure;

namespace Souffleur.Api.Features.Accounting;

public static class AccountingPolicies
{
    /// <summary>Wer buchen und Stammdaten pflegen darf.</summary>
    public const string Book = "Accounting.Book";

    /// <summary>Wer Buchungen lesen darf.</summary>
    public const string Read = "Accounting.Read";

    public static IServiceCollection AddAccountingAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(Book, policy => policy.RequireClaim(
                PermissionClaimsFactory.ClaimType, ((int)Permission.FinanceBook).ToString()))
            .AddPolicy(Read, policy => policy.RequireClaim(
                PermissionClaimsFactory.ClaimType, ((int)Permission.FinanceRead).ToString()));

        return services;
    }
}
