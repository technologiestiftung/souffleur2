using Microsoft.AspNetCore.Mvc;
using Souffleur.Api.Features.Accounting;
using Souffleur.Api.Infrastructure;
using Souffleur.Domain.Banking;
using Souffleur.Domain.Modules;
using Souffleur.Infrastructure.Banking;

namespace Souffleur.Api.Features.Banking;

/// <summary>
/// Konten, Auszüge und der Abgleich.
/// </summary>
/// <remarks>
/// Noch ohne Verbindung zur Bank: FinTS braucht den ARPA-Stand von libfintx,
/// ein echtes Konto und ein TAN-Gerät (siehe
/// <c>docs/bankanbindung-uebernahme-aus-arpa.md</c>). Der Auszug kommt als
/// Datei; alles darunter hängt nicht daran.
/// </remarks>
public static class BankEndpoints
{
    public static IEndpointRouteBuilder MapBankEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/banking")
            .WithTags("Bank")
            .RequireModule(SouffleurModule.Finance);

        group.MapGet("/accounts", ListAccountsAsync).RequireAuthorization(AccountingPolicies.Read);
        group.MapPost("/accounts", CreateAccountAsync).RequireAuthorization(AccountingPolicies.Book);
        group.MapPut("/accounts/{id:guid}", UpdateAccountAsync)
             .RequireAuthorization(AccountingPolicies.Book);
        group.MapDelete("/accounts/{id:guid}", DeleteAccountAsync)
             .RequireAuthorization(AccountingPolicies.Book);

        group.MapPost("/accounts/{id:guid}/statements", ImportAsync)
             .RequireAuthorization(AccountingPolicies.Book)
             .DisableAntiforgery();

        group.MapGet("/transactions", ListTransactionsAsync)
             .RequireAuthorization(AccountingPolicies.Read);
        group.MapGet("/transactions/{id:guid}/suggestions", SuggestAsync)
             .RequireAuthorization(AccountingPolicies.Read);
        group.MapPost("/transactions/{id:guid}/assign", AssignAsync)
             .RequireAuthorization(AccountingPolicies.Book);

        return app;
    }

    private static async Task<IResult> ListAccountsAsync(
        BankService bank,
        [FromQuery] bool includeInactive = false,
        CancellationToken token = default) =>
        TypedResults.Ok(
            (await bank.ListAccountsAsync(includeInactive, token))
            .Select(BankAccountResponse.From));

    private static async Task<IResult> CreateAccountAsync(
        [FromBody] BankAccountRequest request, BankService bank, CancellationToken token)
    {
        var account = new BankAccount { Name = request.Name.Trim(), Iban = request.Iban };
        Apply(request, account);

        try
        {
            await bank.AddAccountAsync(account, token);
        }
        catch (BankException exception)
        {
            return Rejected(exception);
        }

        return TypedResults.Created(
            $"/api/banking/accounts/{account.Id}", BankAccountResponse.From(account));
    }

    private static async Task<IResult> UpdateAccountAsync(
        Guid id, [FromBody] BankAccountRequest request, BankService bank, CancellationToken token)
    {
        try
        {
            BankAccount updated = await bank.UpdateAccountAsync(id, account =>
            {
                account.Name = request.Name.Trim();
                account.Iban = request.Iban;
                Apply(request, account);
            }, token);

            return TypedResults.Ok(BankAccountResponse.From(updated));
        }
        catch (BankException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> DeleteAccountAsync(
        Guid id, BankService bank, CancellationToken token)
    {
        try
        {
            await bank.DeleteAccountAsync(id, token);
        }
        catch (BankException exception)
        {
            return Rejected(exception);
        }

        return TypedResults.NoContent();
    }

    /// <summary>Einen Auszug einlesen. Die Datei kommt als Formular.</summary>
    private static async Task<IResult> ImportAsync(
        Guid id, HttpRequest request, BankService bank, CancellationToken token)
    {
        if (!request.HasFormContentType)
        {
            return TypedResults.Problem(
                "Der Auszug wird als Formular mit Datei erwartet.",
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        IFormFile? file = (await request.ReadFormAsync(token)).Files.GetFile("file");

        if (file is null)
        {
            return Rejected(new BankException("Es fehlt die Datei."));
        }

        try
        {
            await using Stream content = file.OpenReadStream();
            ImportResult result = await bank.ImportAsync(id, content, token);

            return TypedResults.Ok(new ImportResponse(
                result.Added, result.AlreadyKnown, result.Balance, result.BalanceDate));
        }
        catch (Camt053Exception exception)
        {
            return Rejected(new BankException(exception.Message));
        }
        catch (BankException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> ListTransactionsAsync(
        BankService bank,
        [FromQuery] Guid? account = null,
        [FromQuery] bool unassignedOnly = false,
        [FromQuery] int take = 200,
        CancellationToken token = default) =>
        TypedResults.Ok(
            (await bank.ListTransactionsAsync(account, unassignedOnly, take, token))
            .Select(BankTransactionResponse.From));

    private static async Task<IResult> SuggestAsync(
        Guid id, BankService bank, CancellationToken token)
    {
        try
        {
            return TypedResults.Ok(await bank.SuggestAsync(id, token));
        }
        catch (BankException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> AssignAsync(
        Guid id, [FromBody] AssignRequest request, BankService bank, CancellationToken token)
    {
        try
        {
            await bank.AssignAsync(id, request.VoucherId, request.JournalEntryId, token);
        }
        catch (BankException exception)
        {
            return Rejected(exception);
        }

        return TypedResults.NoContent();
    }

    private static void Apply(BankAccountRequest request, BankAccount account)
    {
        account.Bic = Trimmed(request.Bic);
        account.BankName = Trimmed(request.BankName);
        account.AccountId = request.AccountId;

        if (request.IsActive is { } active)
        {
            account.IsActive = active;
        }
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IResult Rejected(BankException exception) =>
        TypedResults.ValidationProblem(
            new Dictionary<string, string[]> { ["Bank"] = [.. exception.Problems] },
            title: "Die Angabe wurde nicht angenommen.");
}

public record BankAccountRequest(
    string Name,
    string Iban,
    string? Bic = null,
    string? BankName = null,
    Guid? AccountId = null,
    bool? IsActive = null);

public record BankAccountResponse(
    Guid Id,
    string Name,
    string Iban,
    string IbanGrouped,
    string? Bic,
    string? BankName,
    Guid? AccountId,
    string? AccountLabel,
    decimal? Balance,
    DateOnly? BalanceDate,
    bool IsActive)
{
    public static BankAccountResponse From(BankAccount a) => new(
        a.Id, a.Name, a.Iban, a.IbanGrouped, a.Bic, a.BankName, a.AccountId,
        a.Account is null ? null : $"{a.Account.Number} {a.Account.Name}",
        a.Balance, a.BalanceDate, a.IsActive);
}

public record ImportResponse(int Added, int AlreadyKnown, decimal? Balance, DateOnly? BalanceDate);

public record AssignRequest(Guid? VoucherId = null, Guid? JournalEntryId = null);

public record BankTransactionResponse(
    Guid Id,
    Guid BankAccountId,
    DateOnly BookedOn,
    DateOnly? ValueDate,
    decimal Amount,
    string Currency,
    string? Purpose,
    string? CounterpartyName,
    string? CounterpartyIban,
    string? EndToEndId,
    Guid? VoucherId,
    string? VoucherLabel,
    Guid? JournalEntryId,
    bool IsAssigned)
{
    public static BankTransactionResponse From(BankTransaction t) => new(
        t.Id, t.BankAccountId, t.BookedOn, t.ValueDate, t.Amount, t.Currency, t.Purpose,
        t.CounterpartyName, t.CounterpartyIban, t.EndToEndId, t.VoucherId,
        t.Voucher?.Label, t.JournalEntryId, t.IsAssigned);
}
