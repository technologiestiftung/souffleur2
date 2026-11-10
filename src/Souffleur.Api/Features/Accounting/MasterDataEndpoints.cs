using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Accounting;
using Souffleur.Api.Infrastructure;
using Souffleur.Domain.Modules;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Api.Features.Accounting;

/// <summary>
/// Kontenrahmen und Kostenstellen. Der Kontenrahmen wird nicht mitgeliefert,
/// sondern vom Steuerbuero uebernommen -- entweder einzeln angelegt oder als
/// CSV eingelesen.
/// </summary>
public static class MasterDataEndpoints
{
    public static IEndpointRouteBuilder MapAccountingMasterDataEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder accounts = app.MapGroup("/api/accounting/accounts")
            .WithTags("Buchhaltung: Konten")
            .RequireModule(SouffleurModule.Finance);

        accounts.MapGet("/", ListAccountsAsync).RequireAuthorization(AccountingPolicies.Read);
        accounts.MapPost("/", CreateAccountAsync).RequireAuthorization(AccountingPolicies.Book);
        accounts.MapPut("/{id:guid}", UpdateAccountAsync).RequireAuthorization(AccountingPolicies.Book);
        accounts.MapPost("/import", ImportAccountsAsync).RequireAuthorization(AccountingPolicies.Book);

        RouteGroupBuilder costCenters = app.MapGroup("/api/accounting/cost-centers")
            .WithTags("Buchhaltung: Kostenstellen")
            .RequireModule(SouffleurModule.Finance);

        costCenters.MapGet("/", ListCostCentersAsync).RequireAuthorization(AccountingPolicies.Read);
        costCenters.MapPost("/", CreateCostCenterAsync).RequireAuthorization(AccountingPolicies.Book);

        return app;
    }

    private static async Task<IResult> ListAccountsAsync(
        SouffleurDbContext context,
        [FromQuery] bool includeInactive = false)
    {
        List<AccountResponse> accounts = await context.Accounts
            .Where(a => includeInactive || a.IsActive)
            .OrderBy(a => a.Number)
            .Select(a => new AccountResponse(a.Id, a.Number, a.Name, a.Type, a.TaxKey, a.DefaultTaxSphere, a.IsActive))
            .ToListAsync();

        return TypedResults.Ok(accounts);
    }

    private static async Task<IResult> CreateAccountAsync(
        [FromBody] AccountRequest request,
        SouffleurDbContext context)
    {
        if (Problems(request.Number, request.Name, request.Type) is { } problems)
        {
            return problems;
        }

        request = request with { Number = request.Number.Trim(), Name = request.Name.Trim() };

        if (await context.Accounts.AnyAsync(a => a.Number == request.Number))
        {
            return TypedResults.Conflict($"Konto {request.Number} gibt es schon.");
        }

        var account = new Account
        {
            Number = request.Number,
            Name = request.Name,
            Type = request.Type,
            TaxKey = request.TaxKey,
            DefaultTaxSphere = request.DefaultTaxSphere,
        };

        context.Accounts.Add(account);
        await context.SaveChangesAsync();

        return TypedResults.Created(
            $"/api/accounting/accounts/{account.Id}",
            new AccountResponse(account.Id, account.Number, account.Name, account.Type, account.TaxKey, account.DefaultTaxSphere, account.IsActive));
    }

    /// <summary>
    /// Name, Art, Steuerschluessel und Sphaere aendern, stilllegen oder wieder
    /// aufnehmen.
    /// </summary>
    /// <remarks>
    /// Die Nummer bleibt: Buchungen, Belege und DATEV-Stapel verweisen auf sie.
    /// Wer eine andere Nummer braucht, legt ein neues Konto an und legt das alte
    /// still. Geloescht wird ein Konto nie -- alte Buchungen zeigen darauf.
    /// </remarks>
    private static async Task<IResult> UpdateAccountAsync(
        Guid id,
        [FromBody] AccountUpdateRequest request,
        SouffleurDbContext context)
    {
        Account? account = await context.Accounts.FirstOrDefaultAsync(a => a.Id == id);
        if (account is null)
        {
            return TypedResults.NotFound();
        }

        if (Problems(account.Number, request.Name, request.Type) is { } problems)
        {
            return problems;
        }

        account.Name = request.Name.Trim();
        account.Type = request.Type;
        account.TaxKey = string.IsNullOrWhiteSpace(request.TaxKey) ? null : request.TaxKey.Trim();
        account.DefaultTaxSphere = request.DefaultTaxSphere;
        account.IsActive = request.IsActive;
        await context.SaveChangesAsync();

        return TypedResults.Ok(new AccountResponse(
            account.Id, account.Number, account.Name, account.Type, account.TaxKey, account.DefaultTaxSphere, account.IsActive));
    }

    private static IResult? Problems(string? number, string? name, AccountType type)
    {
        List<string> problems = [];
        if (string.IsNullOrWhiteSpace(number))
        {
            problems.Add("Eine Kontonummer ist nötig.");
        }
        else if (!number.Trim().All(char.IsAsciiDigit))
        {
            problems.Add("Die Kontonummer besteht nur aus Ziffern.");
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            problems.Add("Ein Name ist nötig.");
        }
        if (!Enum.IsDefined(type))
        {
            problems.Add("Die Kontenart ist unbekannt.");
        }

        return problems.Count == 0
            ? null
            : TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["Konto"] = [.. problems] });
    }

    /// <summary>
    /// Liest die Kontenliste des Steuerbueros ein. Die Oberflaeche liest die CSV
    /// (Nummer;Name;Art;Steuerschluessel) und schickt die Zeilen. Vorhandene
    /// Konten bleiben unberuehrt, unbrauchbare Zeilen werden genannt statt still
    /// angelegt -- ein Konto ohne Namen landete sonst im DATEV-Stapel.
    /// </summary>
    private static async Task<IResult> ImportAccountsAsync(
        [FromBody] AccountImportRequest request,
        SouffleurDbContext context)
    {
        HashSet<string> existing = await context.Accounts
            .Select(a => a.Number)
            .ToHashSetAsync();

        List<Account> added = [];
        List<string> skipped = [];
        List<string> rejected = [];

        foreach (AccountRequest raw in request.Accounts)
        {
            if (Problems(raw.Number, raw.Name, raw.Type) is not null)
            {
                rejected.Add(string.IsNullOrWhiteSpace(raw.Number) ? "(ohne Nummer)" : raw.Number.Trim());
                continue;
            }

            AccountRequest row = raw with { Number = raw.Number.Trim(), Name = raw.Name.Trim() };
            if (!existing.Add(row.Number))
            {
                skipped.Add(row.Number);
                continue;
            }

            added.Add(new Account
            {
                Number = row.Number,
                Name = row.Name,
                Type = row.Type,
                TaxKey = row.TaxKey,
                DefaultTaxSphere = row.DefaultTaxSphere,
            });
        }

        context.Accounts.AddRange(added);
        await context.SaveChangesAsync();

        return TypedResults.Ok(new AccountImportResponse(added.Count, skipped, rejected));
    }

    private static async Task<IResult> ListCostCentersAsync(SouffleurDbContext context)
    {
        List<CostCenterResponse> costCenters = await context.CostCenters
            .Where(c => c.IsActive)
            .OrderBy(c => c.Number)
            .Select(c => new CostCenterResponse(c.Id, c.Number, c.Name))
            .ToListAsync();

        return TypedResults.Ok(costCenters);
    }

    private static async Task<IResult> CreateCostCenterAsync(
        [FromBody] CostCenterRequest request,
        SouffleurDbContext context)
    {
        if (await context.CostCenters.AnyAsync(c => c.Number == request.Number))
        {
            return TypedResults.Conflict($"Kostenstelle {request.Number} gibt es schon.");
        }

        var costCenter = new CostCenter { Number = request.Number, Name = request.Name };
        context.CostCenters.Add(costCenter);
        await context.SaveChangesAsync();

        return TypedResults.Created(
            $"/api/accounting/cost-centers/{costCenter.Id}",
            new CostCenterResponse(costCenter.Id, costCenter.Number, costCenter.Name));
    }
}

public record AccountRequest(string Number, string Name, AccountType Type, string? TaxKey, TaxSphere? DefaultTaxSphere = null);

public record AccountUpdateRequest(string Name, AccountType Type, string? TaxKey, TaxSphere? DefaultTaxSphere, bool IsActive);

public record AccountResponse(Guid Id, string Number, string Name, AccountType Type, string? TaxKey, TaxSphere? DefaultTaxSphere, bool IsActive);

public record AccountImportRequest(IReadOnlyList<AccountRequest> Accounts);

public record AccountImportResponse(int Added, IReadOnlyList<string> SkippedBecauseTheyExist, IReadOnlyList<string> Rejected);

public record CostCenterRequest(string Number, string Name);

public record CostCenterResponse(Guid Id, string Number, string Name);
