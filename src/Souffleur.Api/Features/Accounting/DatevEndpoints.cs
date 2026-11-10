using Microsoft.AspNetCore.Mvc;
using Souffleur.Api.Infrastructure;
using Souffleur.Domain.Accounting;
using Souffleur.Domain.Modules;
using Souffleur.Infrastructure.Accounting.Datev;

namespace Souffleur.Api.Features.Accounting;

/// <summary>
/// Übergabe ans Steuerbüro als DATEV-Buchungsstapel.
/// </summary>
public static class DatevEndpoints
{
    public static IEndpointRouteBuilder MapDatevEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/accounting/datev")
            .WithTags("Buchhaltung: DATEV")
            .RequireModule(SouffleurModule.Finance);

        group.MapGet("/preview", PreviewAsync).RequireAuthorization(AccountingPolicies.Read);
        group.MapGet("/history", HistoryAsync).RequireAuthorization(AccountingPolicies.Read);
        group.MapPost("/export", ExportAsync).RequireAuthorization(AccountingPolicies.Book);

        return app;
    }

    /// <summary>
    /// Was ein Export umfassen würde -- und was ihm im Weg steht. Verändert nichts.
    /// </summary>
    private static async Task<IResult> PreviewAsync(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        DatevExportService datev) =>
        TypedResults.Ok(await datev.PreviewAsync(from, to));

    private static async Task<IResult> HistoryAsync(DatevExportService datev)
    {
        List<DatevExport> history = await datev.HistoryAsync();

        return TypedResults.Ok(history.Select(e => new DatevExportResponse(
            e.Id, e.Label, e.From, e.To, e.FileName, e.EntryCount, e.DebitTotal, e.Sha256, e.ExportedAt)));
    }

    /// <summary>
    /// Erzeugt den Stapel und gibt ihn als Datei zurück. Dabei werden die
    /// enthaltenen Buchungen festgeschrieben -- das ist keine Nebenwirkung,
    /// sondern der Zweck.
    /// </summary>
    private static async Task<IResult> ExportAsync(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        DatevExportService datev)
    {
        DatevResult result;
        try
        {
            result = await datev.ExportAsync(from, to);
        }
        catch (DatevException exception)
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["Export"] = [.. exception.Problems] },
                title: "Der Stapel wurde nicht erzeugt.");
        }

        return TypedResults.File(
            result.Content,
            contentType: "text/csv",
            fileDownloadName: result.Record.FileName);
    }
}

public record DatevExportResponse(
    Guid Id,
    string Label,
    DateOnly From,
    DateOnly To,
    string FileName,
    int EntryCount,
    decimal DebitTotal,
    string Sha256,
    DateTimeOffset ExportedAt);
