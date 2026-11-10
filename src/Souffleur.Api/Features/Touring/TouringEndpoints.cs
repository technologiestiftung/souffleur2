using Microsoft.AspNetCore.Mvc;
using Souffleur.Api.Infrastructure;
using Souffleur.Domain.Calendar;
using Souffleur.Domain.Identity;
using Souffleur.Domain.Modules;
using Souffleur.Domain.Touring;
using Souffleur.Infrastructure.Touring;

namespace Souffleur.Api.Features.Touring;

/// <summary>
/// Tourneen und Gastspiele des Hauses.
/// </summary>
/// <remarks>
/// Haengt am Modul Touring: Ein ortsfestes Haus schaltet es ab und sieht
/// davon nichts mehr. Lesen darf jeder Angemeldete -- wer mitreist, will
/// wissen, wann es losgeht. Anlegen und aendern darf, wer Tourneen plant.
/// </remarks>
public static class TouringEndpoints
{
    public static IEndpointRouteBuilder MapTouringEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/touring")
            .WithTags("Touring")
            .RequireModule(SouffleurModule.Touring)
            .RequireAuthorization();

        group.MapGet("/statuses", ListStatuses);
        group.MapGet("/tours", ListAsync);
        group.MapGet("/tours/{id:guid}", GetAsync);
        group.MapGet("/stops/{id:guid}", GetStopAsync);

        RouteGroupBuilder planning = group.MapGroup("/")
            .RequirePermission(Permission.TouringManage);

        planning.MapPost("/tours", CreateAsync);
        planning.MapPut("/tours/{id:guid}", UpdateAsync);
        planning.MapDelete("/tours/{id:guid}", DeleteAsync);
        planning.MapPost("/tours/{id:guid}/stops", CreateStopAsync);
        planning.MapPut("/stops/{id:guid}", UpdateStopAsync);
        planning.MapDelete("/stops/{id:guid}", DeleteStopAsync);

        return app;
    }

    private static IResult ListStatuses() =>
        TypedResults.Ok(TourStopStatuses.All.Select(status => new StatusInfo(
            status, status.ToString(), status.Label(), status.IsBinding())));

    private static async Task<IResult> ListAsync(
        TouringService touring,
        [FromQuery] bool includeSettled = true,
        CancellationToken token = default)
    {
        List<Tour> tours = await touring.ListAsync(includeSettled, token);
        return TypedResults.Ok(tours.Select(TourResponse.From));
    }

    private static async Task<IResult> GetAsync(Guid id, TouringService touring, CancellationToken token) =>
        await touring.FindAsync(id, token) is { } tour
            ? TypedResults.Ok(TourResponse.From(tour))
            : TypedResults.NotFound();

    private static async Task<IResult> GetStopAsync(Guid id, TouringService touring, CancellationToken token) =>
        await touring.FindStopAsync(id, token) is { } stop
            ? TypedResults.Ok(StopResponse.From(stop, stop.Tour.Title))
            : TypedResults.NotFound();

    private static async Task<IResult> CreateAsync(
        [FromBody] TourRequest request, TouringService touring, CancellationToken token)
    {
        var tour = new Tour { Title = request.Title.Trim() };
        Apply(request, tour);

        try
        {
            await touring.AddAsync(tour, Members(request), token);
        }
        catch (TouringException exception)
        {
            return Rejected(exception);
        }

        Tour created = await touring.FindAsync(tour.Id, token) ?? tour;
        return TypedResults.Created($"/api/touring/tours/{tour.Id}", TourResponse.From(created));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, [FromBody] TourRequest request, TouringService touring, CancellationToken token)
    {
        try
        {
            Tour updated = await touring.UpdateAsync(id, tour =>
            {
                tour.Title = request.Title.Trim();
                Apply(request, tour);
            }, Members(request), token);

            Tour reloaded = await touring.FindAsync(updated.Id, token) ?? updated;
            return TypedResults.Ok(TourResponse.From(reloaded));
        }
        catch (TouringException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> DeleteAsync(Guid id, TouringService touring, CancellationToken token)
    {
        try
        {
            await touring.DeleteAsync(id, token);
        }
        catch (TouringException exception)
        {
            return Rejected(exception);
        }

        return TypedResults.NoContent();
    }

    private static async Task<IResult> CreateStopAsync(
        Guid id, [FromBody] StopRequest request, TouringService touring, CancellationToken token)
    {
        var stop = new TourStop { VenueId = request.VenueId };
        Apply(request, stop);

        try
        {
            await touring.AddStopAsync(id, stop, Performances(request), token);
        }
        catch (TouringException exception)
        {
            return Rejected(exception);
        }

        TourStop created = await touring.FindStopAsync(stop.Id, token) ?? stop;
        return TypedResults.Created(
            $"/api/touring/stops/{stop.Id}",
            StopResponse.From(created, created.Tour?.Title ?? string.Empty));
    }

    private static async Task<IResult> UpdateStopAsync(
        Guid id, [FromBody] StopRequest request, TouringService touring, CancellationToken token)
    {
        try
        {
            TourStop updated = await touring.UpdateStopAsync(id, stop =>
            {
                stop.VenueId = request.VenueId;
                Apply(request, stop);
            }, Performances(request), token);

            TourStop reloaded = await touring.FindStopAsync(updated.Id, token) ?? updated;
            return TypedResults.Ok(StopResponse.From(reloaded, reloaded.Tour?.Title ?? string.Empty));
        }
        catch (TouringException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> DeleteStopAsync(Guid id, TouringService touring, CancellationToken token)
    {
        try
        {
            await touring.DeleteStopAsync(id, token);
        }
        catch (TouringException exception)
        {
            return Rejected(exception);
        }

        return TypedResults.NoContent();
    }

    private static void Apply(TourRequest request, Tour tour)
    {
        tour.ProductionId = request.ProductionId;
        tour.Notes = Clean(request.Notes);
    }

    private static void Apply(StopRequest request, TourStop stop)
    {
        stop.ContactPersonId = request.ContactPersonId;
        stop.Status = request.Status ?? stop.Status;
        stop.ArrivalDate = request.ArrivalDate;
        stop.DepartureDate = request.DepartureDate;
        stop.Fee = request.Fee;
        stop.HostCoversTravel = request.HostCoversTravel ?? false;
        stop.HostCoversAccommodation = request.HostCoversAccommodation ?? false;
        stop.VoucherId = request.VoucherId;
        stop.Notes = Clean(request.Notes);
    }

    private static List<MemberInput>? Members(TourRequest request) =>
        request.Members?.Select(m => new MemberInput(
            m.ContactId, m.FunctionId, m.Status ?? ParticipationStatus.Assigned, Clean(m.Note))).ToList();

    private static List<PerformanceInput>? Performances(StopRequest request) =>
        request.Performances?.Select(p => new PerformanceInput(p.StartsAt, p.EndsAt, Clean(p.Note))).ToList();

    private static string? Clean(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static IResult Rejected(TouringException exception) =>
        TypedResults.ValidationProblem(
            new Dictionary<string, string[]> { ["Touring"] = [.. exception.Problems] },
            title: "Das wurde nicht angenommen.");
}

public record StatusInfo(TourStopStatus Status, string Key, string Label, bool IsBinding);

public record MemberRequest(Guid ContactId, Guid? FunctionId, ParticipationStatus? Status, string? Note);

public record TourRequest(
    string Title,
    Guid? ProductionId,
    string? Notes,
    List<MemberRequest>? Members);

public record PerformanceRequest(DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Note);

public record StopRequest(
    Guid VenueId,
    Guid? ContactPersonId,
    TourStopStatus? Status,
    DateOnly? ArrivalDate,
    DateOnly? DepartureDate,
    decimal? Fee,
    bool? HostCoversTravel,
    bool? HostCoversAccommodation,
    Guid? VoucherId,
    string? Notes,
    List<PerformanceRequest>? Performances);

public record MemberResponse(
    Guid Id,
    Guid ContactId,
    string ContactName,
    Guid? FunctionId,
    string? FunctionLabel,
    ParticipationStatus Status,
    string StatusLabel,
    string? Note)
{
    public static MemberResponse From(TourMember m) => new(
        m.Id, m.ContactId, m.Contact?.Name ?? string.Empty, m.FunctionId, m.Function?.Label,
        m.Status, m.Status.Label(), m.Note);
}

public record PerformanceResponse(Guid Id, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Note)
{
    public static PerformanceResponse From(TourPerformance p) => new(p.Id, p.StartsAt, p.EndsAt, p.Note);
}

public record StopResponse(
    Guid Id,
    Guid TourId,
    string Title,
    Guid VenueId,
    string VenueName,
    string? VenueCity,
    Guid? ContactPersonId,
    string? ContactPersonName,
    TourStopStatus Status,
    string StatusLabel,
    bool IsBinding,
    DateOnly? ArrivalDate,
    DateOnly? DepartureDate,
    decimal? Fee,
    bool HostCoversTravel,
    bool HostCoversAccommodation,
    Guid? VoucherId,
    string? VoucherLabel,
    string? Notes,
    IReadOnlyList<PerformanceResponse> Performances)
{
    public static StopResponse From(TourStop s, string tourTitle) => new(
        s.Id, s.TourId, s.DisplayTitle(tourTitle), s.VenueId, s.Venue?.Name ?? string.Empty, s.Venue?.City,
        s.ContactPersonId, s.ContactPerson?.Name, s.Status, s.Status.Label(), s.Status.IsBinding(),
        s.ArrivalDate, s.DepartureDate, s.Fee, s.HostCoversTravel, s.HostCoversAccommodation,
        s.VoucherId, s.Voucher?.Label, s.Notes,
        [.. s.Performances.Select(PerformanceResponse.From)]);
}

public record TourResponse(
    Guid Id,
    string Title,
    Guid? ProductionId,
    string? ProductionLabel,
    string? Notes,
    DateOnly? StartsOn,
    DateOnly? EndsOn,
    IReadOnlyList<StopResponse> Stops,
    IReadOnlyList<MemberResponse> Members)
{
    public static TourResponse From(Tour t) => new(
        t.Id, t.Title, t.ProductionId, t.Production?.Title, t.Notes,
        t.Span?.From, t.Span?.To,
        [.. t.Stops.Select(s => StopResponse.From(s, t.Title))],
        [.. t.Members.Select(MemberResponse.From)]);
}
