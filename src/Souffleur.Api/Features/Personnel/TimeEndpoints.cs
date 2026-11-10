using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Souffleur.Api.Infrastructure;
using Souffleur.Domain.Contacts;
using Souffleur.Domain.Identity;
using Souffleur.Domain.Modules;
using Souffleur.Domain.Personnel;
using Souffleur.Infrastructure.Personnel;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Api.Features.Personnel;

/// <summary>
/// Kommen und Gehen.
/// </summary>
/// <remarks>
/// Die eigenen Zeiten sieht und stempelt jeder selbst. Fremde Zeiten sieht und
/// traegt nur, wer Dienste plant oder das Haus fuehrt -- Arbeitszeiten sind
/// Personaldaten, und wer wann da war, geht die Kollegin am Nebentisch nichts
/// an.
/// </remarks>
public static class TimeEndpoints
{
    public static IEndpointRouteBuilder MapTimeEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/personnel/times")
            .WithTags("Zeiten")
            .RequireModule(SouffleurModule.Personnel)
            .RequireAuthorization();

        group.MapGet("/me", MineAsync);
        group.MapPost("/kommen", ClockInAsync);
        group.MapPost("/gehen", ClockOutAsync);
        group.MapDelete("/laufend", DiscardAsync);
        group.MapGet("/", ListAsync);
        group.MapPost("/", AddAsync);
        group.MapPut("/{id:guid}", CorrectAsync);

        return app;
    }

    /// <summary>Der eigene Stand: wer ich bin und ob gerade etwas laeuft.</summary>
    private static async Task<IResult> MineAsync(
        TimeClockService times,
        ICurrentUser user,
        CancellationToken token)
    {
        if (user.Id is not { } userId)
        {
            return TypedResults.Unauthorized();
        }

        Contact? contact = await times.ContactOfAsync(userId, token);

        if (contact is null)
        {
            // Kein Fehler, sondern ein Zustand: Das Konto ist noch keinem
            // Kontakt zugeordnet. Die Oberflaeche sagt, was zu tun ist.
            return TypedResults.Ok(new MeResponse(null, null, null));
        }

        TimeEntry? running = await times.RunningAsync(contact.Id, token);

        return TypedResults.Ok(new MeResponse(
            contact.Id,
            contact.Name,
            running is null ? null : TimeResponse.Of(running)));
    }

    private static async Task<IResult> ClockInAsync(
        [FromBody] ClockInRequest request,
        TimeClockService times,
        ICurrentUser user,
        CancellationToken token)
    {
        Guid? contactId = await ResolveAsync(request.ContactId, times, user, token);

        if (contactId is not { } id)
        {
            return Unassigned();
        }

        try
        {
            TimeEntry entry = await times.ClockInAsync(
                id, request.FunctionId, request.EventId, Trimmed(request.Note), token);

            return TypedResults.Ok(TimeResponse.Of(entry));
        }
        catch (TimeEntryException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> ClockOutAsync(
        [FromBody] ClockOutRequest request,
        TimeClockService times,
        ICurrentUser user,
        CancellationToken token)
    {
        Guid? contactId = await ResolveAsync(request.ContactId, times, user, token);

        if (contactId is not { } id)
        {
            return Unassigned();
        }

        try
        {
            TimeEntry entry = await times.ClockOutAsync(id, request.BreakMinutes, token);
            return TypedResults.Ok(TimeResponse.Of(entry));
        }
        catch (TimeEntryException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> DiscardAsync(
        TimeClockService times,
        ICurrentUser user,
        CancellationToken token)
    {
        Guid? contactId = await ResolveAsync(null, times, user, token);

        if (contactId is not { } id)
        {
            return Unassigned();
        }

        try
        {
            await times.DiscardRunningAsync(id, token);
            return TypedResults.NoContent();
        }
        catch (TimeEntryException exception)
        {
            return Rejected(exception);
        }
    }

    /// <summary>
    /// Zeiten eines Zeitraums. Ohne <c>contactId</c> die eigenen; fremde nur
    /// fuer die Dienstplanung und die Leitung.
    /// </summary>
    private static async Task<IResult> ListAsync(
        TimeClockService times,
        ICurrentUser user,
        IAuthorizationService authorization,
        HttpContext http,
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        [FromQuery] Guid? contactId = null,
        [FromQuery] bool all = false,
        CancellationToken token = default)
    {
        bool maySeeOthers = MaySeeOthers(http);

        if ((all || contactId is not null) && !maySeeOthers)
        {
            return TypedResults.Forbid();
        }

        Guid? wanted = contactId;

        if (!all && wanted is null)
        {
            wanted = await ResolveAsync(null, times, user, token);

            if (wanted is null)
            {
                return Unassigned();
            }
        }

        List<TimeEntry> entries = await times.ListAsync(wanted, from, to, token);

        return TypedResults.Ok(entries.Select(TimeResponse.Of).ToList());
    }

    private static async Task<IResult> AddAsync(
        [FromBody] TimeRequest request,
        TimeClockService times,
        ICurrentUser user,
        HttpContext http,
        CancellationToken token)
    {
        Guid? contactId = await ResolveAsync(request.ContactId, times, user, token);

        if (contactId is not { } id)
        {
            return Unassigned();
        }

        if (request.ContactId is { } wanted && wanted != id && !MaySeeOthers(http))
        {
            return TypedResults.Forbid();
        }

        try
        {
            TimeEntry entry = await times.AddAsync(request.ToInput(id), token);
            return TypedResults.Ok(TimeResponse.Of(entry));
        }
        catch (TimeEntryException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> CorrectAsync(
        Guid id,
        [FromBody] CorrectionRequest request,
        TimeClockService times,
        ICurrentUser user,
        HttpContext http,
        CancellationToken token)
    {
        Guid? contactId = await ResolveAsync(request.Entry.ContactId, times, user, token);

        if (contactId is not { } own)
        {
            return Unassigned();
        }

        if (request.Entry.ContactId is { } wanted && wanted != own && !MaySeeOthers(http))
        {
            return TypedResults.Forbid();
        }

        try
        {
            TimeEntry entry = await times.CorrectAsync(
                id, request.Entry.ToInput(own), request.Reason, token);

            return TypedResults.Ok(TimeResponse.Of(entry));
        }
        catch (TimeEntryException exception)
        {
            return Rejected(exception);
        }
    }

    /// <summary>
    /// Wessen Zeit gemeint ist: die angefragte, sonst die eigene.
    /// </summary>
    private static async Task<Guid?> ResolveAsync(
        Guid? wanted,
        TimeClockService times,
        ICurrentUser user,
        CancellationToken token)
    {
        if (wanted is { } id)
        {
            return id;
        }

        if (user.Id is not { } userId)
        {
            return null;
        }

        Contact? contact = await times.ContactOfAsync(userId, token);
        return contact?.Id;
    }

    private static bool MaySeeOthers(HttpContext http) =>
        http.User.IsInRole(Roles.Administrator)
        || http.User.IsInRole(Roles.Management)
        || http.User.IsInRole(Roles.Scheduling);

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IResult Unassigned() =>
        TypedResults.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["Zeit"] =
                [
                    "Dieses Konto gehört noch zu keinem Kontakt. Die Verwaltung "
                    + "verbindet beides im Adressbuch, dann lässt sich stempeln.",
                ],
            },
            title: "Die Zeit wurde nicht angenommen.");

    private static IResult Rejected(TimeEntryException exception) =>
        TypedResults.ValidationProblem(
            new Dictionary<string, string[]> { ["Zeit"] = [.. exception.Problems] },
            title: "Die Zeit wurde nicht angenommen.");
}

public record MeResponse(Guid? ContactId, string? Name, TimeResponse? Running);

public record ClockInRequest(Guid? ContactId, Guid? FunctionId, Guid? EventId, string? Note);

public record ClockOutRequest(Guid? ContactId, int BreakMinutes);

public record TimeRequest(
    Guid? ContactId,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    int BreakMinutes,
    Guid? FunctionId,
    Guid? EventId,
    string? Note)
{
    public TimeEntryInput ToInput(Guid contactId) => new(
        contactId, StartedAt, EndedAt, BreakMinutes, FunctionId, EventId,
        string.IsNullOrWhiteSpace(Note) ? null : Note.Trim());
}

public record CorrectionRequest(TimeRequest Entry, string Reason);

public record TimeResponse(
    Guid Id,
    Guid ContactId,
    string? ContactName,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    int BreakMinutes,
    int? WorkedMinutes,
    Guid? FunctionId,
    string? FunctionLabel,
    Guid? EventId,
    string? Note,
    int Source,
    string SourceLabel,
    string? CorrectionReason,
    IReadOnlyList<string> Notices)
{
    public static TimeResponse Of(TimeEntry entry) => new(
        entry.Id,
        entry.ContactId,
        entry.Contact?.Name,
        entry.StartedAt,
        entry.EndedAt,
        entry.BreakMinutes,
        entry.WorkedMinutes,
        entry.FunctionId,
        entry.Function?.Label,
        entry.EventId,
        entry.Note,
        (int)entry.Source,
        entry.Source.Label(),
        entry.CorrectionReason,
        entry.Notices());
}
