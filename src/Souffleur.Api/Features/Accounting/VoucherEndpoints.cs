using Souffleur.Infrastructure.Documents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Accounting;
using Souffleur.Infrastructure.Accounting;
using Souffleur.Api.Infrastructure;
using Souffleur.Domain.Modules;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Api.Features.Accounting;

/// <summary>
/// Belege: aufnehmen, ansehen, richtigstellen, mit einer Buchung verbinden.
/// Geloescht wird nie -- Belege unterliegen der Aufbewahrungspflicht. Die Datei
/// aendert sich auch beim Richtigstellen nicht, nur die Angaben darum herum.
/// </summary>
public static class VoucherEndpoints
{
    public static IEndpointRouteBuilder MapVoucherEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/accounting/vouchers")
            .WithTags("Buchhaltung: Belege")
            .RequireModule(SouffleurModule.Finance);

        group.MapGet("/", ListAsync).RequireAuthorization(AccountingPolicies.Read);
        group.MapGet("/{id:guid}", GetAsync).RequireAuthorization(AccountingPolicies.Read);
        group.MapGet("/{id:guid}/file", DownloadAsync).RequireAuthorization(AccountingPolicies.Read);
        group.MapPost("/", UploadAsync).RequireAuthorization(AccountingPolicies.Book)
             .DisableAntiforgery();
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(AccountingPolicies.Book);
        group.MapPost("/{id:guid}/link/{journalEntryId:guid}", LinkAsync)
             .RequireAuthorization(AccountingPolicies.Book);
        group.MapGet("/integrity", CheckIntegrityAsync).RequireAuthorization(AccountingPolicies.Read);

        return app;
    }

    private static async Task<IResult> ListAsync(
        SouffleurDbContext context,
        [FromQuery] int? fiscalYear = null,
        [FromQuery] bool? unbookedOnly = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100)
    {
        IQueryable<Voucher> query = context.Vouchers.AsNoTracking().Include(v => v.File);

        if (fiscalYear is { } year)
        {
            query = query.Where(v => v.FiscalYear == year);
        }

        if (unbookedOnly == true)
        {
            query = query.Where(v => v.JournalEntryId == null);
        }

        List<VoucherResponse> vouchers = await query
            .OrderByDescending(v => v.FiscalYear).ThenByDescending(v => v.Number)
            .Skip(skip).Take(Math.Clamp(take, 1, 500))
            .Select(v => VoucherResponse.From(v))
            .ToListAsync();

        return TypedResults.Ok(vouchers);
    }

    private static async Task<IResult> GetAsync(Guid id, SouffleurDbContext context)
    {
        Voucher? voucher = await context.Vouchers.AsNoTracking()
            .Include(v => v.File)
            .FirstOrDefaultAsync(v => v.Id == id);

        return voucher is null ? TypedResults.NotFound() : TypedResults.Ok(VoucherResponse.From(voucher));
    }

    /// <summary>
    /// Nimmt einen Beleg entgegen: die Datei plus die Angaben als Formularfelder.
    /// </summary>
    private static async Task<IResult> UploadAsync(
        HttpRequest request,
        VoucherService vouchers,
        FolderService folders,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            return TypedResults.Problem(
                "Der Beleg wird als Formular mit Datei erwartet.",
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        IFormCollection form = await request.ReadFormAsync(cancellationToken);
        IFormFile? file = form.Files.GetFile("file");

        if (!TryReadFields(form, out VoucherFields fields, out string? fieldError))
        {
            return TypedResults.Problem(fieldError, statusCode: StatusCodes.Status400BadRequest);
        }

        var voucher = new Voucher
        {
            FiscalYear = fields.FiscalYear,
            Date = fields.Date,
            GrossAmount = fields.GrossAmount,
            Description = fields.Description,
            ExternalNumber = fields.ExternalNumber,
            DueDate = fields.DueDate,
            ContactId = fields.ContactId,
            ProductionId = fields.ProductionId,
            Direction = fields.Direction,
            Source = fields.Source,
        };

        try
        {
            await using Stream? content = file?.OpenReadStream();
            await vouchers.AddAsync(
                voucher, content, file?.FileName, file?.ContentType, cancellationToken);
        }
        catch (VoucherException exception)
        {
            return Rejected(exception.Problems);
        }
        catch (VoucherFileException exception)
        {
            return Rejected([exception.Message]);
        }

        // Eine lesbare Kopie in den Finanzordner, wenn das Haus einen hat
        // (ADR-0013). Scheitert sie, steht der Beleg trotzdem.
        if (voucher.File is not null)
        {
            await folders.CopyVoucherAsync(voucher.Id, cancellationToken);
        }

        return TypedResults.Created($"/api/accounting/vouchers/{voucher.Id}", VoucherResponse.From(voucher));
    }

    private static async Task<IResult> DownloadAsync(
        Guid id,
        SouffleurDbContext context,
        VoucherStorage storage)
    {
        Voucher? voucher = await context.Vouchers.AsNoTracking()
            .Include(v => v.File)
            .FirstOrDefaultAsync(v => v.Id == id);

        if (voucher?.File is not { } file)
        {
            return TypedResults.NotFound();
        }

        if (!storage.Exists(file))
        {
            return TypedResults.Problem(
                $"Zu Beleg {voucher.Label} ist die Datei nicht auffindbar. Das gehört geprüft.",
                statusCode: StatusCodes.Status410Gone);
        }

        // Nicht im Browser ausführen lassen, was als Beleg hochgeladen wurde.
        return TypedResults.File(
            storage.OpenRead(file),
            contentType: file.ContentType,
            fileDownloadName: $"{voucher.Label}-{file.OriginalName}");
    }

    private static async Task<IResult> LinkAsync(
        Guid id,
        Guid journalEntryId,
        VoucherService vouchers)
    {
        try
        {
            await vouchers.LinkAsync(id, journalEntryId);
        }
        catch (VoucherException exception)
        {
            return Rejected(exception.Problems);
        }

        return TypedResults.NoContent();
    }

    /// <summary>
    /// Rechnet die Pruefwerte aller Belegdateien nach. Gehoert spaeter auf die
    /// Systemzustandsseite, damit es jemand bemerkt, bevor es die Pruefung tut.
    /// </summary>
    private static async Task<IResult> CheckIntegrityAsync(
        VoucherService vouchers,
        [FromQuery] int? fiscalYear = null) =>
        TypedResults.Ok(await vouchers.CheckFilesAsync(fiscalYear));

    /// <summary>
    /// Stellt die Angaben eines Belegs richtig. Die Datei bleibt unberuehrt --
    /// darum nimmt dieser Weg auch keine entgegen.
    /// </summary>
    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] VoucherUpdateRequest request,
        VoucherService vouchers,
        CancellationToken cancellationToken)
    {
        try
        {
            Voucher voucher = await vouchers.UpdateAsync(
                id,
                new VoucherChanges(
                    request.Date,
                    request.GrossAmount,
                    request.Description,
                    request.ExternalNumber,
                    request.DueDate,
                    request.ContactId,
                    request.Direction,
                    request.ProductionId),
                cancellationToken);

            return TypedResults.Ok(VoucherResponse.From(voucher));
        }
        catch (VoucherException problem)
        {
            return Rejected(problem.Problems);
        }
    }

    private static bool TryReadFields(IFormCollection form, out VoucherFields fields, out string? error)
    {
        fields = default;
        error = null;

        if (!int.TryParse(form["fiscalYear"], out int fiscalYear))
        {
            error = "Das Geschäftsjahr fehlt oder ist keine Zahl.";
            return false;
        }

        if (!DateOnly.TryParse(form["date"], out DateOnly date))
        {
            error = "Das Belegdatum fehlt oder ist kein Datum.";
            return false;
        }

        if (!decimal.TryParse(
                form["grossAmount"],
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out decimal gross))
        {
            error = "Der Bruttobetrag fehlt oder ist keine Zahl.";
            return false;
        }

        DateOnly? dueDate = DateOnly.TryParse(form["dueDate"], out DateOnly due) ? due : null;
        Guid? contactId = Guid.TryParse(form["contactId"], out Guid contact) ? contact : null;
        Guid? productionId = Guid.TryParse(form["productionId"], out Guid production) ? production : null;

        fields = new VoucherFields(
            fiscalYear,
            date,
            gross,
            form["description"].ToString(),
            string.IsNullOrWhiteSpace(form["externalNumber"]) ? null : form["externalNumber"].ToString(),
            dueDate,
            contactId,
            productionId,
            Enum.TryParse(form["direction"], out VoucherDirection direction) ? direction : VoucherDirection.Incoming,
            Enum.TryParse(form["source"], out VoucherSource source) ? source : VoucherSource.Upload);

        return true;
    }

    private static IResult Rejected(IReadOnlyList<string> problems) =>
        TypedResults.ValidationProblem(
            new Dictionary<string, string[]> { ["Beleg"] = [.. problems] },
            title: "Der Beleg wurde nicht angenommen.");

    private readonly record struct VoucherFields(
        int FiscalYear,
        DateOnly Date,
        decimal GrossAmount,
        string Description,
        string? ExternalNumber,
        DateOnly? DueDate,
        Guid? ContactId,
        Guid? ProductionId,
        VoucherDirection Direction,
        VoucherSource Source);
}

/// <summary>
/// Die Angaben eines Belegs, richtiggestellt. Ohne Belegnummer, Geschaeftsjahr
/// und Herkunft: Die stehen fest, sobald der Beleg im Haus ist.
/// </summary>
public record VoucherUpdateRequest(
    DateOnly Date,
    decimal GrossAmount,
    string Description,
    VoucherDirection Direction,
    string? ExternalNumber = null,
    DateOnly? DueDate = null,
    Guid? ContactId = null,
    Guid? ProductionId = null);

public record VoucherFileResponse(string OriginalName, string ContentType, long SizeInBytes, string Sha256);

public record VoucherResponse(
    Guid Id,
    string Label,
    int Number,
    int FiscalYear,
    DateOnly Date,
    decimal GrossAmount,
    string Description,
    string? ExternalNumber,
    DateOnly? DueDate,
    Guid? ContactId,
    Guid? ProductionId,
    VoucherDirection Direction,
    VoucherSource Source,
    bool IsBooked,
    bool IsCommitted,
    Guid? JournalEntryId,
    VoucherFileResponse? File)
{
    public static VoucherResponse From(Voucher v) => new(
        v.Id, v.Label, v.Number, v.FiscalYear, v.Date, v.GrossAmount, v.Description,
        v.ExternalNumber, v.DueDate, v.ContactId, v.ProductionId, v.Direction, v.Source,
        v.JournalEntryId is not null, v.CommittedAt is not null, v.JournalEntryId,
        v.File is null ? null : new VoucherFileResponse(
            v.File.OriginalName, v.File.ContentType, v.File.SizeInBytes, v.File.Sha256));
}
