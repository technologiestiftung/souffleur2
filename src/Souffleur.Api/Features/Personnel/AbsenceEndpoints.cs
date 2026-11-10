using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Souffleur.Api.Features.Notifications;
using Souffleur.Api.Infrastructure;
using Souffleur.Domain.Contacts;
using Souffleur.Domain.Identity;
using Souffleur.Domain.Modules;
using Souffleur.Domain.Personnel;
using Souffleur.Infrastructure.Identity;
using Souffleur.Infrastructure.Notifications;
using Souffleur.Infrastructure.Personnel;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Api.Features.Personnel;

/// <summary>
/// Urlaub, Krankheit und was sonst fehlen laesst -- dazu die Vertragsdaten,
/// aus denen Urlaubs- und Stundenkonto folgen.
/// </summary>
/// <remarks>
/// Drei Kreise, wie bei den Zeiten (ADR-0009, ADR-0012):
///
/// Jede Person sieht und beantragt das Eigene. Wer Antraege entscheidet
/// (<see cref="Permission.PersonnelApprove"/>), sieht alle Abwesenheiten mit
/// Grund, auch Krankmeldungen. Wer Vertraege pflegt
/// (<see cref="Permission.PersonnelRecords"/>), sieht Konten und Stammdaten,
/// aber nicht, warum jemand fehlt -- das ist ein anderes Recht, weil es
/// Gesundheitsdaten beruehrt.
///
/// Alle anderen im Haus sehen im Kalender nur, dass jemand fehlt.
/// </remarks>
public static class AbsenceEndpoints
{
    public static IEndpointRouteBuilder MapAbsenceEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/personnel/absences")
            .WithTags("Abwesenheiten")
            .RequireModule(SouffleurModule.Personnel)
            .RequireAuthorization();

        group.MapGet("/kinds", ListKinds);
        group.MapGet("/me", MeAsync);
        group.MapGet("/", ListAsync);
        group.MapPost("/", RequestAsync);
        group.MapPost("/{id:guid}/withdraw", WithdrawAsync);
        group.MapPut("/{id:guid}/sickness", UpdateSicknessAsync);
        group.MapGet("/balance", BalanceAsync);
        group.MapGet("/holidays", HolidaysAsync);
        group.MapGet("/employments/{contactId:guid}", EmploymentsAsync);
        group.MapGet("/adjustments/{contactId:guid}", AdjustmentsAsync);

        RouteGroupBuilder deciding = group.MapGroup("/")
            .RequirePermission(Permission.PersonnelApprove);

        deciding.MapGet("/open", OpenAsync);
        deciding.MapGet("/payroll", PayrollAsync);
        deciding.MapPost("/{id:guid}/decide", DecideAsync);
        deciding.MapPut("/{id:guid}/payroll", MarkPayrollAsync);

        group.MapGet("/team", TeamAsync)
            .RequireAnyPermission(Permission.PersonnelApprove, Permission.PersonnelRecords);

        RouteGroupBuilder records = group.MapGroup("/")
            .RequirePermission(Permission.PersonnelRecords);

        records.MapPost("/employments", AddEmploymentAsync);
        records.MapPut("/employments/{id:guid}", UpdateEmploymentAsync);
        records.MapDelete("/employments/{id:guid}", RemoveEmploymentAsync);
        records.MapPost("/adjustments", AddAdjustmentAsync);
        records.MapDelete("/adjustments/{id:guid}", RemoveAdjustmentAsync);
        records.MapGet("/house-holidays", HouseHolidaysAsync);
        records.MapPost("/house-holidays", AddHouseHolidayAsync);
        records.MapDelete("/house-holidays/{id:guid}", RemoveHouseHolidayAsync);

        return app;
    }

    private static IResult ListKinds() =>
        TypedResults.Ok(AbsenceKinds.All.Select(kind => new KindInfo(
            (int)kind, kind.Label(), kind.NeedsApproval(), kind.IsSickness(),
            kind.CountsAgainstLeave(), kind.ReducesHours())));

    private static async Task<IResult> MeAsync(
        AbsenceService absences,
        ICurrentUser user,
        CancellationToken token)
    {
        Contact? contact = user.Id is { } id ? await absences.ContactOfAsync(id, token) : null;
        return TypedResults.Ok(new AbsenceMeResponse(contact?.Id, contact?.Name));
    }

    /// <summary>
    /// Abwesenheiten eines Zeitraums. Ohne Person die eigenen; fremde und alle
    /// nur fuer die, die entscheiden.
    /// </summary>
    private static async Task<IResult> ListAsync(
        AbsenceService absences,
        ICurrentUser user,
        ClaimsPrincipal principal,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? contactId = null,
        [FromQuery] bool all = false,
        CancellationToken token = default)
    {
        Guid? own = await OwnAsync(absences, user, token);
        bool mayDecide = principal.Has(Permission.PersonnelApprove);

        if ((all || (contactId is { } wanted && wanted != own)) && !mayDecide)
        {
            return TypedResults.Forbid();
        }

        Guid? person = all ? null : contactId ?? own;

        if (!all && person is null)
        {
            return Unassigned();
        }

        List<Absence> found = await absences.ListAsync(person, from, to, token);
        return TypedResults.Ok(await ResponsesAsync(absences, found, token));
    }

    private static async Task<IResult> OpenAsync(AbsenceService absences, CancellationToken token) =>
        TypedResults.Ok(await ResponsesAsync(absences, await absences.OpenRequestsAsync(token), token));

    private static async Task<IResult> PayrollAsync(AbsenceService absences, CancellationToken token) =>
        TypedResults.Ok(await ResponsesAsync(absences, await absences.UnreportedToPayrollAsync(token), token));

    /// <summary>
    /// Beantragen oder krank melden. Fuer jemand anderen nur, wer entscheidet
    /// -- die Kollegin, die fuer den Kollegen mit Fieber anruft, gibt es der
    /// Leitung durch.
    /// </summary>
    private static async Task<IResult> RequestAsync(
        [FromBody] AbsenceRequest request,
        AbsenceService absences,
        AccessService access,
        NotificationService glocke,
        ICurrentUser user,
        ClaimsPrincipal principal,
        CancellationToken token)
    {
        Guid? own = await OwnAsync(absences, user, token);
        Guid? person = request.ContactId ?? own;

        if (person is not { } contactId)
        {
            return Unassigned();
        }

        if (contactId != own && !principal.Has(Permission.PersonnelApprove))
        {
            return TypedResults.Forbid();
        }

        if (!Enum.IsDefined(request.Kind))
        {
            return Rejected("Diese Art von Abwesenheit gibt es nicht.");
        }

        try
        {
            Absence absence = await absences.RequestAsync(
                new AbsenceInput(
                    contactId, request.Kind, request.StartDate, request.EndDate,
                    request.IsHalfDay, request.DeputyId, request.Note, request.CertificateReceived),
                token);

            await Ausloeser.AbwesenheitGemeldetAsync(
                glocke,
                await access.UsersWithPermissionAsync(Permission.PersonnelApprove, token),
                absence,
                token);

            return TypedResults.Ok(await ResponseAsync(absences, absence, token));
        }
        catch (AbsenceException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> DecideAsync(
        Guid id,
        [FromBody] DecisionRequest request,
        AbsenceService absences,
        AccessService access,
        NotificationService glocke,
        ICurrentUser user,
        CancellationToken token)
    {
        Absence? absence = await absences.FindAsync(id, token);
        if (absence is null)
        {
            return TypedResults.NotFound();
        }

        // Ueber den eigenen Urlaub entscheidet jemand anderes -- es sei denn,
        // es gibt niemanden sonst. In einem Haus mit einer Leitung waere der
        // Antrag sonst nicht zu bescheiden.
        if (await OwnAsync(absences, user, token) == absence.ContactId)
        {
            List<Guid> deciders = await access.UsersWithPermissionAsync(Permission.PersonnelApprove, token);
            if (deciders.Any(d => d != user.Id))
            {
                return Rejected("Über eigene Anträge entscheidet jemand anderes.");
            }
        }

        try
        {
            absence = await absences.DecideAsync(id, request.Approve, request.Note, user.Id, token);
            await Ausloeser.AbwesenheitEntschiedenAsync(glocke, absence, token);
            return TypedResults.Ok(await ResponseAsync(absences, absence, token));
        }
        catch (AbsenceException exception)
        {
            return Rejected(exception);
        }
    }

    /// <summary>
    /// Zurueckziehen. Die Person selbst: einen offenen Antrag immer, einen
    /// genehmigten nur vor seinem Beginn, eine Krankmeldung, wenn sie falsch
    /// war. Wer entscheidet: alles, mit Grund.
    /// </summary>
    private static async Task<IResult> WithdrawAsync(
        Guid id,
        [FromBody] WithdrawRequest request,
        AbsenceService absences,
        AccessService access,
        NotificationService glocke,
        ICurrentUser user,
        ClaimsPrincipal principal,
        CancellationToken token)
    {
        Absence? absence = await absences.FindAsync(id, token);
        if (absence is null)
        {
            return TypedResults.NotFound();
        }

        bool isOwn = await OwnAsync(absences, user, token) == absence.ContactId;
        bool mayDecide = principal.Has(Permission.PersonnelApprove);

        if (!isOwn && !mayDecide)
        {
            return TypedResults.Forbid();
        }

        if (isOwn && !mayDecide
            && absence.Status == AbsenceStatus.Approved
            && absence.StartDate <= await absences.TodayAsync(token))
        {
            return Rejected("Ein Urlaub, der schon begonnen hat, lässt sich nur über die Leitung aufheben.");
        }

        try
        {
            (Absence withdrawn, AbsenceStatus before) =
                await absences.WithdrawAsync(id, request.Reason, user.Id, token);

            if (before != AbsenceStatus.Requested || !isOwn)
            {
                List<Guid> recipients = isOwn
                    ? await access.UsersWithPermissionAsync(Permission.PersonnelApprove, token)
                    : await glocke.UsersOfContactsAsync([withdrawn.ContactId], token);

                await Ausloeser.AbwesenheitAufgehobenAsync(glocke, recipients, withdrawn, token);
            }

            return TypedResults.Ok(await ResponseAsync(absences, withdrawn, token));
        }
        catch (AbsenceException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> UpdateSicknessAsync(
        Guid id,
        [FromBody] SicknessRequest request,
        AbsenceService absences,
        ICurrentUser user,
        ClaimsPrincipal principal,
        CancellationToken token)
    {
        Absence? absence = await absences.FindAsync(id, token);
        if (absence is null)
        {
            return TypedResults.NotFound();
        }

        if (await OwnAsync(absences, user, token) != absence.ContactId
            && !principal.Has(Permission.PersonnelApprove))
        {
            return TypedResults.Forbid();
        }

        try
        {
            absence = await absences.UpdateSicknessAsync(id, request.EndDate, request.CertificateReceived, token);
            return TypedResults.Ok(await ResponseAsync(absences, absence, token));
        }
        catch (AbsenceException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> MarkPayrollAsync(
        Guid id,
        [FromBody] PayrollRequest request,
        AbsenceService absences,
        CancellationToken token)
    {
        try
        {
            Absence absence = await absences.MarkPayrollAsync(id, request.Reported, token);
            return TypedResults.Ok(await ResponseAsync(absences, absence, token));
        }
        catch (AbsenceException exception)
        {
            return Rejected(exception);
        }
    }

    // --- Konten ---

    private static async Task<IResult> BalanceAsync(
        AbsenceService absences,
        ICurrentUser user,
        ClaimsPrincipal principal,
        [FromQuery] int year,
        [FromQuery] Guid? contactId = null,
        CancellationToken token = default)
    {
        Guid? own = await OwnAsync(absences, user, token);
        Guid? person = contactId ?? own;

        if (person is not { } id)
        {
            return Unassigned();
        }

        if (id != own && !MaySeeAccounts(principal))
        {
            return TypedResults.Forbid();
        }

        try
        {
            return TypedResults.Ok(BalanceResponse.Of(await absences.BalanceAsync(id, year, token)));
        }
        catch (AbsenceException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> TeamAsync(
        AbsenceService absences,
        [FromQuery] int year,
        CancellationToken token) =>
        TypedResults.Ok((await absences.TeamAsync(year, token)).Select(BalanceResponse.Of));

    private static async Task<IResult> HolidaysAsync(
        AbsenceService absences,
        [FromQuery] int year,
        CancellationToken token) =>
        TypedResults.Ok(await absences.HolidaysAsync(year, token));

    // --- Vertraege ---

    private static async Task<IResult> EmploymentsAsync(
        Guid contactId,
        AbsenceService absences,
        ICurrentUser user,
        ClaimsPrincipal principal,
        CancellationToken token)
    {
        if (contactId != await OwnAsync(absences, user, token) && !MaySeeAccounts(principal))
        {
            return TypedResults.Forbid();
        }

        return TypedResults.Ok((await absences.EmploymentsAsync(contactId, token)).Select(EmploymentResponse.Of));
    }

    private static Task<IResult> AddEmploymentAsync(
        [FromBody] EmploymentRequest request,
        AbsenceService absences,
        CancellationToken token) =>
        SaveEmploymentAsync(null, request, absences, token);

    private static Task<IResult> UpdateEmploymentAsync(
        Guid id,
        [FromBody] EmploymentRequest request,
        AbsenceService absences,
        CancellationToken token) =>
        SaveEmploymentAsync(id, request, absences, token);

    private static async Task<IResult> SaveEmploymentAsync(
        Guid? id,
        EmploymentRequest request,
        AbsenceService absences,
        CancellationToken token)
    {
        try
        {
            Employment employment = await absences.SaveEmploymentAsync(
                id,
                new Employment
                {
                    ContactId = request.ContactId,
                    ValidFrom = request.ValidFrom,
                    EndsOn = request.EndsOn,
                    WeeklyHours = request.WeeklyHours,
                    WorkDays = (WorkWeekdays)request.WorkDays,
                    AnnualLeaveDays = request.AnnualLeaveDays,
                    Note = request.Note,
                },
                token);

            return TypedResults.Ok(EmploymentResponse.Of(employment));
        }
        catch (AbsenceException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> RemoveEmploymentAsync(
        Guid id, AbsenceService absences, CancellationToken token)
    {
        await absences.RemoveEmploymentAsync(id, token);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> AdjustmentsAsync(
        Guid contactId,
        AbsenceService absences,
        ICurrentUser user,
        ClaimsPrincipal principal,
        [FromQuery] int year,
        CancellationToken token)
    {
        if (contactId != await OwnAsync(absences, user, token) && !MaySeeAccounts(principal))
        {
            return TypedResults.Forbid();
        }

        return TypedResults.Ok((await absences.AdjustmentsAsync(contactId, year, token))
            .Select(a => new AdjustmentResponse(a.Id, a.ContactId, a.Year, a.Days, a.Reason, a.CreatedAt)));
    }

    private static async Task<IResult> AddAdjustmentAsync(
        [FromBody] AdjustmentRequest request,
        AbsenceService absences,
        CancellationToken token)
    {
        try
        {
            LeaveAdjustment a = await absences.AddAdjustmentAsync(
                request.ContactId, request.Year, request.Days, request.Reason, token);
            return TypedResults.Ok(new AdjustmentResponse(a.Id, a.ContactId, a.Year, a.Days, a.Reason, a.CreatedAt));
        }
        catch (AbsenceException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> RemoveAdjustmentAsync(
        Guid id, AbsenceService absences, CancellationToken token)
    {
        await absences.RemoveAdjustmentAsync(id, token);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> HouseHolidaysAsync(AbsenceService absences, CancellationToken token) =>
        TypedResults.Ok((await absences.HouseHolidaysAsync(token))
            .Select(h => new HouseHolidayResponse(h.Id, h.Month, h.Day, h.Name)));

    private static async Task<IResult> AddHouseHolidayAsync(
        [FromBody] HouseHolidayRequest request,
        AbsenceService absences,
        CancellationToken token)
    {
        try
        {
            HouseHoliday h = await absences.AddHouseHolidayAsync(
                request.Month, request.Day, request.Name ?? "", token);
            return TypedResults.Ok(new HouseHolidayResponse(h.Id, h.Month, h.Day, h.Name));
        }
        catch (AbsenceException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> RemoveHouseHolidayAsync(
        Guid id, AbsenceService absences, CancellationToken token)
    {
        await absences.RemoveHouseHolidayAsync(id, token);
        return TypedResults.NoContent();
    }

    // --- Helfer ---

    private static bool MaySeeAccounts(ClaimsPrincipal principal) =>
        principal.Has(Permission.PersonnelApprove) || principal.Has(Permission.PersonnelRecords);

    private static async Task<Guid?> OwnAsync(AbsenceService absences, ICurrentUser user, CancellationToken token) =>
        user.Id is { } id ? (await absences.ContactOfAsync(id, token))?.Id : null;

    private static async Task<AbsenceResponse> ResponseAsync(
        AbsenceService absences, Absence absence, CancellationToken token) =>
        (await ResponsesAsync(absences, [absence], token))[0];

    private static async Task<List<AbsenceResponse>> ResponsesAsync(
        AbsenceService absences, List<Absence> found, CancellationToken token)
    {
        Dictionary<Guid, decimal> days = await absences.DaysOfAsync(found, token);
        return [.. found.Select(a => AbsenceResponse.Of(a, days.GetValueOrDefault(a.Id)))];
    }

    private static IResult Unassigned() =>
        TypedResults.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["Abwesenheit"] =
                [
                    "Dieses Konto gehört noch zu keinem Kontakt. Die Verwaltung "
                    + "verbindet beides im Adressbuch, dann lässt sich beantragen.",
                ],
            },
            title: "Die Abwesenheit wurde nicht angenommen.");

    private static IResult Rejected(AbsenceException exception) =>
        TypedResults.ValidationProblem(
            new Dictionary<string, string[]> { ["Abwesenheit"] = [.. exception.Problems] },
            title: "Die Abwesenheit wurde nicht angenommen.");

    private static IResult Rejected(string problem) => Rejected(new AbsenceException(problem));
}

public record KindInfo(
    int Value, string Label, bool NeedsApproval, bool IsSickness, bool CountsAgainstLeave, bool ReducesHours);

public record AbsenceMeResponse(Guid? ContactId, string? Name);

public record AbsenceRequest(
    Guid? ContactId,
    AbsenceKind Kind,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsHalfDay,
    Guid? DeputyId,
    string? Note,
    bool CertificateReceived);

public record DecisionRequest(bool Approve, string? Note);

public record WithdrawRequest(string? Reason);

public record SicknessRequest(DateOnly EndDate, bool CertificateReceived);

public record PayrollRequest(bool Reported);

public record AbsenceResponse(
    Guid Id,
    Guid ContactId,
    string? ContactName,
    int Kind,
    string KindLabel,
    bool IsSickness,
    bool ConcernsPayroll,
    int Status,
    string StatusLabel,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsHalfDay,
    decimal Days,
    Guid? DeputyId,
    string? DeputyName,
    string? Note,
    bool CertificateReceived,
    DateTimeOffset? DecidedAt,
    string? DecisionNote,
    DateTimeOffset? PayrollReportedAt,
    DateTimeOffset CreatedAt)
{
    public static AbsenceResponse Of(Absence a, decimal days) => new(
        a.Id,
        a.ContactId,
        a.Contact?.Name,
        (int)a.Kind,
        a.Kind.Label(),
        a.Kind.IsSickness(),
        a.Kind.ConcernsPayroll(),
        (int)a.Status,
        a.Status.Label(),
        a.StartDate,
        a.EndDate,
        a.IsHalfDay,
        days,
        a.DeputyId,
        a.Deputy?.Name,
        a.Note,
        a.CertificateReceived,
        a.DecidedAt,
        a.DecisionNote,
        a.PayrollReportedAt,
        a.CreatedAt);
}

public record EmploymentRequest(
    Guid ContactId,
    DateOnly ValidFrom,
    DateOnly? EndsOn,
    decimal WeeklyHours,
    int WorkDays,
    decimal AnnualLeaveDays,
    string? Note);

public record EmploymentResponse(
    Guid Id,
    Guid ContactId,
    DateOnly ValidFrom,
    DateOnly? EndsOn,
    decimal WeeklyHours,
    int WorkDays,
    decimal AnnualLeaveDays,
    int DailyTargetMinutes,
    string? Note)
{
    public static EmploymentResponse Of(Employment e) => new(
        e.Id, e.ContactId, e.ValidFrom, e.EndsOn, e.WeeklyHours, (int)e.WorkDays,
        e.AnnualLeaveDays, e.DailyTargetMinutes, e.Note);
}

public record AdjustmentRequest(Guid ContactId, int Year, decimal Days, string Reason);

public record AdjustmentResponse(Guid Id, Guid ContactId, int Year, decimal Days, string Reason, DateTimeOffset CreatedAt);

public record HouseHolidayRequest(int Month, int Day, string? Name);

public record HouseHolidayResponse(Guid Id, int Month, int Day, string Name);

public record HoursResponse(
    DateOnly From, DateOnly To, int Target, int Credited, int Worked, int Balance, IReadOnlyList<DateOnly> MissingDays);

public record BalanceResponse(
    Guid ContactId,
    string Name,
    EmploymentResponse? Employment,
    int Year,
    decimal Entitlement,
    decimal Adjustments,
    decimal Taken,
    decimal Pending,
    decimal Remaining,
    HoursResponse? Hours)
{
    public static BalanceResponse Of(PersonBalance b) => new(
        b.ContactId,
        b.Name,
        b.Employment is null ? null : EmploymentResponse.Of(b.Employment),
        b.Leave.Year,
        b.Leave.Entitlement,
        b.Leave.Adjustments,
        b.Leave.Taken,
        b.Leave.Pending,
        b.Leave.Remaining,
        b.Hours is { } h
            ? new HoursResponse(h.From, h.To, h.Target, h.Credited, h.Worked, h.Balance, h.MissingDays)
            : null);
}
