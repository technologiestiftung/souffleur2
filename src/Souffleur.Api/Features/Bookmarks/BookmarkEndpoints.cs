using Microsoft.AspNetCore.Mvc;
using Souffleur.Domain.Bookmarks;
using Souffleur.Infrastructure.Bookmarks;

namespace Souffleur.Api.Features.Bookmarks;

/// <summary>
/// Lesezeichen: Adressen im Netz an jedem Gegenstand des Hauses.
/// </summary>
/// <remarks>
/// Ein Endpunkt fuer alle Module, nicht einer je Modul: Die Oberflaeche hat
/// einen Baustein dafuer, der nur Art und Id des Gegenstands kennt. Wer
/// angemeldet ist, darf lesen und schreiben -- ein Lesezeichen verraet
/// nichts, was der Gegenstand nicht ohnehin zeigt, und wer den Gegenstand
/// nicht sieht, kommt auch nicht an seine Id.
/// </remarks>
public static class BookmarkEndpoints
{
    public static IEndpointRouteBuilder MapBookmarkEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/bookmarks")
            .WithTags("Lesezeichen")
            .RequireAuthorization();

        group.MapGet("/{target}/{targetId:guid}", ListAsync);
        group.MapPost("/{target}/{targetId:guid}", AddAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapDelete("/{id:guid}", RemoveAsync);

        return app;
    }

    private static async Task<IResult> ListAsync(
        BookmarkTarget target, Guid targetId, BookmarkService bookmarks, CancellationToken token)
    {
        if (!Enum.IsDefined(target))
        {
            return UnbekanntesZiel();
        }

        return TypedResults.Ok((await bookmarks.ListAsync(target, targetId, token)).Select(BookmarkResponse.From));
    }

    private static async Task<IResult> AddAsync(
        BookmarkTarget target,
        Guid targetId,
        [FromBody] BookmarkRequest request,
        BookmarkService bookmarks,
        CancellationToken token)
    {
        if (!Enum.IsDefined(target))
        {
            return UnbekanntesZiel();
        }

        try
        {
            Bookmark created = await bookmarks.AddAsync(target, targetId, request.Url, request.Label, token);
            return TypedResults.Created($"/api/bookmarks/{created.Id}", BookmarkResponse.From(created));
        }
        catch (BookmarkException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, [FromBody] BookmarkRequest request, BookmarkService bookmarks, CancellationToken token)
    {
        try
        {
            return TypedResults.Ok(BookmarkResponse.From(
                await bookmarks.UpdateAsync(id, request.Url, request.Label, token)));
        }
        catch (BookmarkException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> RemoveAsync(Guid id, BookmarkService bookmarks, CancellationToken token)
    {
        await bookmarks.RemoveAsync(id, token);
        return TypedResults.NoContent();
    }

    private static IResult UnbekanntesZiel() =>
        TypedResults.Problem(
            "An diese Art von Gegenstand lässt sich kein Lesezeichen heften.",
            statusCode: StatusCodes.Status400BadRequest);

    private static IResult Rejected(BookmarkException exception) =>
        TypedResults.ValidationProblem(
            new Dictionary<string, string[]> { ["Lesezeichen"] = [.. exception.Problems] },
            title: "Das Lesezeichen wurde nicht angenommen.");
}

public record BookmarkRequest(string Url, string? Label = null);

public record BookmarkResponse(Guid Id, BookmarkTarget Target, Guid TargetId, string Url, string? Label, int SortOrder)
{
    public static BookmarkResponse From(Bookmark b) => new(b.Id, b.Target, b.TargetId, b.Url, b.Label, b.SortOrder);
}
