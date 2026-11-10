using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Souffleur.Api.Infrastructure;
using Souffleur.Domain.Documents;
using Souffleur.Domain.Identity;
using Souffleur.Domain.Modules;
using Souffleur.Infrastructure.Documents;
using Souffleur.Infrastructure.Identity;
using Souffleur.Infrastructure.Persistence;
using Souffleur.Api.Features.Notifications;
using Souffleur.Infrastructure.Notifications;

namespace Souffleur.Api.Features.Documents;

/// <summary>
/// Die Ablage des Hauses. Lesen verlangt das Leserecht, Ablegen das
/// Schreibrecht; wer fremde Dokumente aendern darf, entscheidet das
/// Verwaltungsrecht -- im Dienst, nicht hier.
/// </summary>
public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/documents")
                                     .WithTags("Dokumente")
                                     .RequireModule(SouffleurModule.Documents);

        group.MapGet("/", ListAsync).RequirePermission(Permission.DocumentsRead);
        group.MapGet("/tags", TagsAsync).RequirePermission(Permission.DocumentsRead);
        group.MapGet("/{id:guid}", GetAsync).RequirePermission(Permission.DocumentsRead);
        group.MapGet("/{id:guid}/file", DownloadAsync).RequirePermission(Permission.DocumentsRead);

        group.MapPost("/", UploadAsync)
             .RequirePermission(Permission.DocumentsWrite)
             .DisableAntiforgery()
             // Kestrel nimmt sonst nur gut 28 MB an -- ein Probenmitschnitt
             // scheiterte, bevor die eigene Pruefung ueberhaupt zu Wort kaeme.
             .WithMetadata(new RequestSizeLimitAttribute(DocumentStorage.MaxSizeInBytes + 1024 * 1024));

        group.MapPut("/{id:guid}", UpdateAsync).RequirePermission(Permission.DocumentsWrite);
        group.MapDelete("/{id:guid}", DeleteAsync).RequirePermission(Permission.DocumentsWrite);
        group.MapPost("/{id:guid}/links", LinkAsync).RequirePermission(Permission.DocumentsWrite);
        group.MapDelete("/{id:guid}/links/{linkId:guid}", UnlinkAsync).RequirePermission(Permission.DocumentsWrite);

        return app;
    }

    private static DocumentViewer Viewer(ClaimsPrincipal user, ICurrentUser currentUser) => new(
        currentUser.Id,
        user.HasClaim(PermissionClaimsFactory.ClaimType, ((int)Permission.DocumentsManage).ToString()));

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal user,
        ICurrentUser currentUser,
        DocumentService documents,
        [FromQuery] string? q = null,
        [FromQuery] string? tag = null,
        [FromQuery] DocumentTarget? target = null,
        [FromQuery] Guid? targetId = null,
        [FromQuery] Guid? folderId = null,
        CancellationToken token = default)
    {
        List<Document> found = await documents.ListAsync(
            Viewer(user, currentUser), q, tag, target, targetId, folderId, token);
        var namen = await documents.LabelsAsync(found.SelectMany(d => d.Links), token);
        return TypedResults.Ok(found.Select(d => DocumentResponse.From(d, namen, currentUser.Id)));
    }

    private static async Task<IResult> TagsAsync(
        ClaimsPrincipal user, ICurrentUser currentUser, DocumentService documents, CancellationToken token) =>
        TypedResults.Ok(await documents.TagsAsync(Viewer(user, currentUser), token));

    private static async Task<IResult> GetAsync(
        Guid id, ClaimsPrincipal user, ICurrentUser currentUser, DocumentService documents, CancellationToken token)
    {
        Document? document = await documents.FindAsync(id, Viewer(user, currentUser), token);
        if (document is null)
        {
            return TypedResults.NotFound();
        }

        var namen = await documents.LabelsAsync(document.Links, token);
        return TypedResults.Ok(DocumentResponse.From(document, namen, currentUser.Id));
    }

    /// <summary>
    /// Was der Browser zur Vorschau anzeigen darf -- nach der Endung, nicht
    /// nach dem gespeicherten Typ: Was die File Station hineinlegt, ist als
    /// application/octet-stream gefuehrt (ADR-0013). Nur Formate, die der
    /// Browser selbst darstellt und die kein Skript ausfuehren: kein HTML,
    /// kein SVG, keine Office-Dateien.
    /// </summary>
    private static readonly Dictionary<string, string> Vorschau = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".heic"] = "image/heic",
        [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav",
        [".m4a"] = "audio/mp4",
        [".mp4"] = "video/mp4",
        [".mov"] = "video/quicktime",
        [".txt"] = "text/plain; charset=utf-8",
        [".csv"] = "text/plain; charset=utf-8",
    };

    /// <summary>
    /// Als Anhang -- oder mit <c>?inline=true</c> zur Vorschau, aber nur fuer die
    /// Formate in <see cref="Vorschau"/>. Alles andere geht auch dann als
    /// Anhang hinaus: Eine Datei, die sich anders ausgibt, als sie ist, soll
    /// nicht als Seite laufen.
    /// </summary>
    private static async Task<IResult> DownloadAsync(
        Guid id,
        ClaimsPrincipal user,
        ICurrentUser currentUser,
        DocumentService documents,
        HttpContext context,
        CancellationToken token,
        bool inline = false)
    {
        Document? document = await documents.FindAsync(id, Viewer(user, currentUser), token);
        if (document is null)
        {
            return TypedResults.NotFound();
        }

        if (!documents.FileExists(document))
        {
            if (document.FolderId is not null)
            {
                return TypedResults.Problem(
                    $"„{document.Name}“ liegt nicht mehr im Ordner {document.Folder?.Path}/{document.FolderPath}. "
                    + "Wurde es in der File Station verschoben, findet souffleur es beim nächsten Einlesen wieder.",
                    statusCode: StatusCodes.Status410Gone);
            }

            return TypedResults.Problem(
                $"Zu „{document.Name}“ ist die Datei nicht auffindbar. Das gehört geprüft.",
                statusCode: StatusCodes.Status410Gone);
        }

        context.Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        string endung = Path.GetExtension(document.OriginalName);
        string name = document.Name.EndsWith(endung, StringComparison.OrdinalIgnoreCase)
            ? document.Name
            : document.Name + endung;

        if (inline && Vorschau.TryGetValue(endung, out string? typ))
        {
            // Bilder, Mitschnitte, Text: Nichts davon braucht Skripte, also
            // ausdruecklich keine. PDF nicht -- der Betrachter des Browsers
            // liefe in einer Sandbox nicht, und er fuehrt ohnehin nichts aus,
            // was die Seite erreicht.
            if (!typ.StartsWith("application/pdf", StringComparison.Ordinal))
            {
                context.Response.Headers[HeaderNames.ContentSecurityPolicy] = "sandbox; default-src 'none'; img-src 'self'; media-src 'self'; style-src 'unsafe-inline'";
            }

            context.Response.Headers[HeaderNames.ContentDisposition] =
                new ContentDispositionHeaderValue("inline") { FileNameStar = name }.ToString();
            context.Response.Headers[HeaderNames.CacheControl] = "private, max-age=300";

            // Bereichsanfragen, damit sich im Video springen laesst.
            return TypedResults.File(documents.OpenRead(document), typ, enableRangeProcessing: true);
        }

        return TypedResults.File(documents.OpenRead(document), document.ContentType, fileDownloadName: name);
    }

    private static async Task<IResult> UploadAsync(
        HttpRequest request,
        DocumentService documents,
        ICurrentUser currentUser,
        NotificationService glocke,
        CancellationToken token)
    {
        if (!request.HasFormContentType)
        {
            return TypedResults.Problem(
                "Das Dokument wird als Formular mit Datei erwartet.",
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        IFormCollection form = await request.ReadFormAsync(token);
        IFormFile? file = form.Files.GetFile("file");
        if (file is null)
        {
            return Rejected(["Es fehlt die Datei."]);
        }

        if (!TryLinks(form["links"], out List<(DocumentTarget, Guid)> links, out string? fehler)
            || !TryGuids(form["groupIds"], out List<Guid> groupIds, out fehler))
        {
            return Rejected([fehler!]);
        }

        // „intern" haelt die Datei bei souffleur, eine Kennung waehlt den
        // Ordner, leer laesst das Modul entscheiden.
        string? ordner = form["folder"].FirstOrDefault();
        FolderChoice choice = ordner switch
        {
            null or "" => FolderChoice.Automatic,
            "intern" => new FolderChoice(Internal: true),
            _ when Guid.TryParse(ordner, out Guid folderId) => new FolderChoice(folderId),
            _ => FolderChoice.Automatic,
        };

        var input = new DocumentInput(
            form["name"].FirstOrDefault(),
            form["note"].FirstOrDefault(),
            [.. form["tags"].Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!)],
            groupIds,
            string.Equals(form["restricted"].FirstOrDefault(), "true", StringComparison.OrdinalIgnoreCase));

        try
        {
            await using Stream content = file.OpenReadStream();
            Document document = await documents.UploadAsync(content, file.FileName, input, links, choice, token);

            // Gleiche Datei schon da? Ein Hinweis, keine Sperre -- vielleicht
            // ist es Absicht, etwa dieselbe Rechnung an zwei Projekten.
            var viewer = new DocumentViewer(currentUser.Id, CanManage: false);
            List<string> gleich = await documents.DuplicatesAsync(document.Id, document.Sha256, viewer, token);

            // Frisch geladen, damit die Gruppen ihre Namen tragen. Wer es
            // gerade abgelegt hat, sieht es in jedem Fall.
            Document geladen = await documents.FindAsync(document.Id, viewer, token) ?? document;

            // Offene Dokumente klingeln nicht -- sonst laeutet bei jedem
            // Programmheft das ganze Haus. Nur wer eigens gemeint ist.
            if (geladen.IsRestricted)
            {
                await Ausloeser.DokumentAsync(
                    glocke, geladen.Name, geladen.Groups.Select(g => (g.GroupId, g.Group?.Name ?? "—")), token);
            }
            var namen = await documents.LabelsAsync(geladen.Links, token);
            return TypedResults.Created(
                $"/api/documents/{document.Id}",
                DocumentResponse.From(geladen, namen, currentUser.Id) with { SameFileAs = gleich });
        }
        catch (DocumentException exception)
        {
            return Rejected(exception.Problems);
        }
        catch (DocumentFileException exception)
        {
            return Rejected([exception.Message]);
        }
        catch (UnauthorizedAccessException)
        {
            return Rejected(["souffleur darf in diesen Ordner nicht schreiben. Die Rechte der Freigabe gehören geprüft."]);
        }
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] DocumentUpdateRequest request,
        ClaimsPrincipal user,
        ICurrentUser currentUser,
        DocumentService documents,
        CancellationToken token)
    {
        try
        {
            bool ok = await documents.UpdateAsync(
                id,
                new DocumentInput(request.Name, request.Note, request.Tags ?? [], request.GroupIds ?? [], request.Restricted),
                Viewer(user, currentUser),
                token);

            // 404 statt 403 auch bei „sehen ja, aendern nein": Das verraet
            // nicht, ob es ein Dokument gibt, das man nicht aendern darf.
            return ok ? TypedResults.NoContent() : TypedResults.NotFound();
        }
        catch (DocumentException exception)
        {
            return Rejected(exception.Problems);
        }
    }

    private static async Task<IResult> DeleteAsync(
        Guid id, ClaimsPrincipal user, ICurrentUser currentUser, DocumentService documents, CancellationToken token) =>
        await documents.DeleteAsync(id, Viewer(user, currentUser), token)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();

    private static async Task<IResult> LinkAsync(
        Guid id,
        [FromBody] DocumentLinkRequest request,
        ClaimsPrincipal user,
        ICurrentUser currentUser,
        DocumentService documents,
        CancellationToken token)
    {
        if (!Enum.IsDefined(request.Target))
        {
            return Rejected(["Dieses Ziel gibt es nicht."]);
        }

        try
        {
            return await documents.LinkAsync(id, request.Target, request.TargetId, Viewer(user, currentUser), token)
                ? TypedResults.NoContent()
                : TypedResults.NotFound();
        }
        catch (DocumentException exception)
        {
            return Rejected(exception.Problems);
        }
    }

    private static async Task<IResult> UnlinkAsync(
        Guid id, Guid linkId, ClaimsPrincipal user, ICurrentUser currentUser, DocumentService documents,
        CancellationToken token) =>
        await documents.UnlinkAsync(id, linkId, Viewer(user, currentUser), token)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();

    // ── Hilfen ───────────────────────────────────────────────────────────

    private static IResult Rejected(IEnumerable<string> problems) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["document"] = [.. problems] });

    /// <summary>Verknuepfungen kommen im Formular als „Contact:guid".</summary>
    private static bool TryLinks(
        Microsoft.Extensions.Primitives.StringValues werte,
        out List<(DocumentTarget, Guid)> links,
        out string? fehler)
    {
        links = [];
        fehler = null;
        foreach (string? wert in werte)
        {
            string[] teile = (wert ?? string.Empty).Split(':', 2);
            if (teile.Length != 2
                || !Enum.TryParse(teile[0], ignoreCase: true, out DocumentTarget art)
                || !Enum.IsDefined(art)
                || !Guid.TryParse(teile[1], out Guid id))
            {
                fehler = $"Die Verknüpfung „{wert}“ ist nicht lesbar.";
                return false;
            }

            links.Add((art, id));
        }

        return true;
    }

    private static bool TryGuids(
        Microsoft.Extensions.Primitives.StringValues werte, out List<Guid> ids, out string? fehler)
    {
        ids = [];
        fehler = null;
        foreach (string? wert in werte)
        {
            if (!Guid.TryParse(wert, out Guid id))
            {
                fehler = "Eine der Gruppen ist nicht lesbar.";
                return false;
            }

            ids.Add(id);
        }

        return true;
    }
}

public record DocumentUpdateRequest(
    string? Name, string? Note, string[]? Tags, Guid[]? GroupIds, bool Restricted);

public record DocumentLinkRequest(DocumentTarget Target, Guid TargetId);

public record DocumentLinkResponse(Guid Id, DocumentTarget Target, string TargetKey, Guid TargetId, string Label);

public record DocumentGroupResponse(Guid Id, string Name);

public record DocumentResponse(
    Guid Id,
    string Name,
    string OriginalName,
    string ContentType,
    long SizeInBytes,
    string? Note,
    bool IsRestricted,
    string[] Tags,
    DocumentGroupResponse[] Groups,
    DocumentLinkResponse[] Links,
    DateTimeOffset CreatedAt,
    bool IsMine,
    Guid? FolderId,
    string? FolderName,
    string? FolderPath,
    bool IsMissing,
    IReadOnlyList<string>? SameFileAs = null)
{
    public static DocumentResponse From(
        Document d,
        IReadOnlyDictionary<(DocumentTarget, Guid), string> namen,
        Guid? userId) => new(
        d.Id,
        d.Name,
        d.OriginalName,
        d.ContentType,
        d.SizeInBytes,
        d.Note,
        d.IsRestricted,
        [.. d.Tags.Select(t => t.Tag).Order()],
        [.. d.Groups.Select(g => new DocumentGroupResponse(g.GroupId, g.Group?.Name ?? "—"))],
        [.. d.Links.Select(l => new DocumentLinkResponse(
            l.Id, l.Target, l.Target.ToString(), l.TargetId,
            namen.GetValueOrDefault((l.Target, l.TargetId)) ?? "nicht mehr vorhanden"))],
        d.CreatedAt,
        d.CreatedBy is not null && d.CreatedBy == userId,
        d.FolderId,
        d.Folder?.Name,
        d.Folder is { } f && d.FolderPath is { } p ? $"{f.Path}/{p}" : null,
        d.MissingSince is not null);
}
