using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Accounting;
using Souffleur.Infrastructure.Accounting;
using Souffleur.Api.Infrastructure;
using Souffleur.Domain.Modules;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Api.Features.Accounting;

/// <summary>
/// Das Journal: buchen, festschreiben, stornieren. Geloescht wird nie.
/// </summary>
public static class JournalEndpoints
{
    public static IEndpointRouteBuilder MapJournalEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/accounting/journal")
            .WithTags("Buchhaltung: Journal")
            .RequireModule(SouffleurModule.Finance);

        group.MapGet("/", ListAsync).RequireAuthorization(AccountingPolicies.Read);
        group.MapGet("/{id:guid}", GetAsync).RequireAuthorization(AccountingPolicies.Read);
        group.MapPost("/", CreateAsync).RequireAuthorization(AccountingPolicies.Book);
        group.MapPost("/{id:guid}/commit", CommitAsync).RequireAuthorization(AccountingPolicies.Book);
        group.MapPost("/{id:guid}/reverse", ReverseAsync).RequireAuthorization(AccountingPolicies.Book);

        return app;
    }

    private static async Task<IResult> ListAsync(
        SouffleurDbContext context,
        [FromQuery] int? fiscalYear = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100)
    {
        IQueryable<JournalEntry> query = context.JournalEntries.AsNoTracking();

        if (fiscalYear is { } year)
        {
            query = query.Where(e => e.FiscalYear == year);
        }

        List<JournalEntryResponse> entries = await query
            .OrderByDescending(e => e.FiscalYear).ThenByDescending(e => e.Number)
            .Skip(skip)
            .Take(Math.Clamp(take, 1, 500))
            .Include(e => e.Lines)
            .Select(e => JournalEntryResponse.From(e))
            .ToListAsync();

        return TypedResults.Ok(entries);
    }

    private static async Task<IResult> GetAsync(Guid id, SouffleurDbContext context)
    {
        JournalEntry? entry = await context.JournalEntries
            .AsNoTracking()
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == id);

        return entry is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(JournalEntryResponse.From(entry));
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] JournalEntryRequest request,
        JournalService journal)
    {
        var entry = new JournalEntry
        {
            FiscalYear = request.FiscalYear,
            Date = request.Date,
            Description = request.Description,
            DocumentNumber = request.DocumentNumber,
            Lines = [.. request.Lines.Select(line => new JournalLine
            {
                AccountId = line.AccountId,
                Debit = line.Debit,
                Credit = line.Credit,
                TaxKey = line.TaxKey,
                TaxAmount = line.TaxAmount,
                TaxSphere = line.TaxSphere,
                CostCenterId = line.CostCenterId,
                ContactId = line.ContactId,
                Description = line.Description,
            })],
        };

        try
        {
            await journal.AddAsync(entry);
        }
        catch (JournalException exception)
        {
            return Rejected(exception);
        }

        return TypedResults.Created(
            $"/api/accounting/journal/{entry.Id}",
            JournalEntryResponse.From(entry));
    }

    private static async Task<IResult> CommitAsync(Guid id, JournalService journal)
    {
        try
        {
            await journal.CommitAsync(id);
        }
        catch (JournalException exception)
        {
            return Rejected(exception);
        }
        catch (InvalidOperationException exception)
        {
            return TypedResults.Problem(exception.Message, statusCode: StatusCodes.Status409Conflict);
        }

        return TypedResults.NoContent();
    }

    private static async Task<IResult> ReverseAsync(
        Guid id,
        [FromBody] ReversalRequest request,
        JournalService journal)
    {
        JournalEntry reversal;
        try
        {
            reversal = await journal.ReverseAsync(id, request.Date, request.Reason);
        }
        catch (JournalException exception)
        {
            return Rejected(exception);
        }
        catch (ArgumentException exception)
        {
            return TypedResults.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        return TypedResults.Created(
            $"/api/accounting/journal/{reversal.Id}",
            JournalEntryResponse.From(reversal));
    }

    /// <summary>
    /// Eine abgelehnte Buchung ist kein Serverfehler, sondern eine fachliche
    /// Antwort -- die Gruende gehoeren einzeln in die Oberflaeche.
    /// </summary>
    private static IResult Rejected(JournalException exception) =>
        TypedResults.ValidationProblem(
            new Dictionary<string, string[]> { ["Buchung"] = [.. exception.Problems] },
            title: "Die Buchung wurde nicht angenommen.");
}

public record JournalLineRequest(
    Guid AccountId,
    decimal Debit,
    decimal Credit,
    string? TaxKey = null,
    decimal TaxAmount = 0m,
    TaxSphere? TaxSphere = null,
    Guid? CostCenterId = null,
    Guid? ContactId = null,
    string? Description = null);

public record JournalEntryRequest(
    int FiscalYear,
    DateOnly Date,
    string Description,
    IReadOnlyList<JournalLineRequest> Lines,
    string? DocumentNumber = null);

public record ReversalRequest(DateOnly Date, string Reason);

public record JournalLineResponse(
    Guid Id,
    Guid AccountId,
    decimal Debit,
    decimal Credit,
    string? TaxKey,
    decimal TaxAmount,
    TaxSphere? TaxSphere,
    Guid? CostCenterId,
    Guid? ContactId,
    string? Description);

public record JournalEntryResponse(
    Guid Id,
    int Number,
    int FiscalYear,
    DateOnly Date,
    string Description,
    string? DocumentNumber,
    bool IsCommitted,
    DateTimeOffset? CommittedAt,
    Guid? ReversalOfId,
    decimal Total,
    IReadOnlyList<JournalLineResponse> Lines)
{
    public static JournalEntryResponse From(JournalEntry e) => new(
        e.Id,
        e.Number,
        e.FiscalYear,
        e.Date,
        e.Description,
        e.DocumentNumber,
        e.CommittedAt is not null,
        e.CommittedAt,
        e.ReversalOfId,
        e.DebitTotal,
        [.. e.Lines.Select(l => new JournalLineResponse(
            l.Id, l.AccountId, l.Debit, l.Credit, l.TaxKey, l.TaxAmount, l.TaxSphere,
            l.CostCenterId, l.ContactId, l.Description))]);
}
