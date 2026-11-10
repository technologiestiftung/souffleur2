using Microsoft.AspNetCore.Mvc;
using Souffleur.Api.Infrastructure;
using Souffleur.Domain.Documents;
using Souffleur.Domain.Identity;
using Souffleur.Domain.Modules;
using Souffleur.Infrastructure.Documents;

namespace Souffleur.Api.Features.Documents;

/// <summary>
/// Die Ordner der Ablage auf der Synology (ADR-0013).
/// </summary>
/// <remarks>
/// Sehen, welche Ordner es gibt, darf jeder, der Dokumente liest -- zum
/// Filtern und zum Waehlen beim Hochladen. Einrichten, durchsuchen und
/// Unterordner anlegen nur, wer fremde Dokumente verwalten darf: Wer einen
/// Ordner eintraegt, macht seine Dateien in souffleur sichtbar.
/// </remarks>
public static class FolderEndpoints
{
    public static IEndpointRouteBuilder MapFolderEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/documents/folders")
            .WithTags("Ordner")
            .RequireModule(SouffleurModule.Documents);

        group.MapGet("/", ListAsync).RequirePermission(Permission.DocumentsRead);

        RouteGroupBuilder manage = group.MapGroup("/").RequirePermission(Permission.DocumentsManage);
        manage.MapGet("/status", Status);
        manage.MapGet("/browse", Browse);
        manage.MapPost("/directories", CreateDirectory);
        manage.MapPost("/", CreateAsync);
        manage.MapPut("/{id:guid}", UpdateAsync);
        manage.MapDelete("/{id:guid}", RemoveAsync);
        manage.MapPost("/{id:guid}/scan", ScanAsync);
        manage.MapPost("/scan", ScanAllAsync);

        // Belege kopieren heisst, Finanzunterlagen in einen Ordner zu legen --
        // dafuer braucht es beides.
        manage.MapPost("/copy-vouchers", CopyVouchersAsync).RequirePermission(Permission.FinanceRead);

        return app;
    }

    private static async Task<IResult> ListAsync(FolderService folders, CancellationToken token)
    {
        List<DocumentFolder> list = await folders.ListAsync(token);
        Dictionary<Guid, (int Files, int Missing)> counts = await folders.CountsAsync(token);
        return TypedResults.Ok(list.Select(f => FolderResponse.From(f, counts.GetValueOrDefault(f.Id))));
    }

    private static IResult Status(FolderStorage storage) =>
        TypedResults.Ok(new FolderStatus(storage.IsAvailable, storage.Root));

    private static IResult Browse(FolderStorage storage, [FromQuery] string? path = null)
    {
        try
        {
            return TypedResults.Ok(storage.Browse(path));
        }
        catch (DocumentFileException exception)
        {
            return Rejected([exception.Message]);
        }
    }

    private static IResult CreateDirectory([FromBody] DirectoryRequest request, FolderStorage storage)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Rejected(["Der neue Ordner braucht einen Namen."]);
        }

        try
        {
            string path = storage.CreateDirectory(request.Parent, request.Name);
            return TypedResults.Ok(new FolderEntry(path.Split('/')[^1], path, false));
        }
        catch (Exception exception) when (exception is DocumentFileException or IOException or UnauthorizedAccessException)
        {
            return Rejected([exception is UnauthorizedAccessException
                ? "souffleur darf dort keinen Ordner anlegen. Die Rechte der Freigabe gehören geprüft."
                : exception.Message]);
        }
    }

    private static Task<IResult> CreateAsync([FromBody] FolderRequest request, FolderService folders, CancellationToken token) =>
        SaveAsync(null, request, folders, token);

    private static Task<IResult> UpdateAsync(
        Guid id, [FromBody] FolderRequest request, FolderService folders, CancellationToken token) =>
        SaveAsync(id, request, folders, token);

    private static async Task<IResult> SaveAsync(
        Guid? id, FolderRequest request, FolderService folders, CancellationToken token)
    {
        if (!Enum.IsDefined(request.Module))
        {
            return Rejected(["Dieses Modul gibt es nicht."]);
        }

        try
        {
            DocumentFolder folder = await folders.SaveAsync(
                id,
                new FolderInput(request.Name ?? string.Empty, request.Path ?? string.Empty, request.Module,
                    request.IsDefault, request.IsRestricted, request.GroupIds ?? []),
                token);

            // Gleich einlesen: Wer einen Ordner eintraegt, will sehen, was darin
            // liegt, und nicht fuenf Minuten auf den Takt warten.
            await folders.ScanAsync(folder.Id, token);

            DocumentFolder? geladen = (await folders.ListAsync(token)).FirstOrDefault(f => f.Id == folder.Id);
            Dictionary<Guid, (int Files, int Missing)> counts = await folders.CountsAsync(token);
            return TypedResults.Ok(FolderResponse.From(geladen ?? folder, counts.GetValueOrDefault(folder.Id)));
        }
        catch (DocumentException exception)
        {
            return Rejected(exception.Problems);
        }
    }

    private static async Task<IResult> RemoveAsync(Guid id, FolderService folders, CancellationToken token)
    {
        await folders.RemoveAsync(id, token);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ScanAsync(Guid id, FolderService folders, CancellationToken token) =>
        TypedResults.Ok(new ScanResponse(await folders.ScanAsync(id, token)));

    private static async Task<IResult> ScanAllAsync(FolderService folders, CancellationToken token)
    {
        Dictionary<Guid, string> results = await folders.ScanAllAsync(token);
        return TypedResults.Ok(results.Select(r => new { FolderId = r.Key, Result = r.Value }));
    }

    private static async Task<IResult> CopyVouchersAsync(FolderService folders, CancellationToken token) =>
        TypedResults.Ok(new { Copied = await folders.CopyAllVouchersAsync(token) });

    private static IResult Rejected(IEnumerable<string> problems) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["folder"] = [.. problems] });
}

public record FolderRequest(
    string? Name,
    string? Path,
    SouffleurModule Module,
    bool IsDefault,
    bool IsRestricted,
    Guid[]? GroupIds);

public record DirectoryRequest(string? Parent, string? Name);

public record FolderStatus(bool Available, string Root);

public record ScanResponse(string Result);

public record FolderResponse(
    Guid Id,
    string Name,
    string Path,
    SouffleurModule Module,
    string ModuleKey,
    string ModuleLabel,
    bool IsDefault,
    bool IsRestricted,
    DocumentGroupResponse[] Groups,
    DateTimeOffset? LastScanAt,
    string? LastScanResult,
    int Files,
    int Missing)
{
    public static FolderResponse From(DocumentFolder f, (int Files, int Missing) counts) => new(
        f.Id,
        f.Name,
        f.Path,
        f.Module,
        f.Module.ToString(),
        f.Module.Label(),
        f.IsDefault,
        f.IsRestricted,
        [.. f.Groups.Select(g => new DocumentGroupResponse(g.GroupId, g.Group?.Name ?? "—"))],
        f.LastScanAt,
        f.LastScanResult,
        counts.Files,
        counts.Missing);
}
