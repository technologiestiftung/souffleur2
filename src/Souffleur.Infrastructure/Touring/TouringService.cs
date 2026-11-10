using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Accounting;
using Souffleur.Domain.Catalogs;
using Souffleur.Domain.Contacts;
using Souffleur.Domain.Touring;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Touring;

public class TouringException(IReadOnlyList<string> problems)
    : Exception(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;

    public TouringException(string problem) : this([problem])
    {
    }
}

/// <summary>Eine gewuenschte Vorstellung, wie sie hereinkommt.</summary>
public sealed record PerformanceInput(DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Note = null);

/// <summary>Jemand, der mitreisen soll, wie es hereinkommt.</summary>
public sealed record MemberInput(
    Guid ContactId,
    Guid? FunctionId = null,
    Domain.Calendar.ParticipationStatus Status = Domain.Calendar.ParticipationStatus.Assigned,
    string? Note = null);

/// <summary>
/// Tourneen und Gastspiele des Hauses.
/// </summary>
/// <remarks>
/// Prueft die Verweise in die anderen Module, bevor etwas gespeichert wird:
/// Die Spielstaette muss ein Kontakt sein, die Produktion eine des Hauses,
/// der Beleg ein Ausgangsbeleg. Die Datenbank sichert die Fremdschluessel,
/// aber nicht den Sinn -- dass niemand eine Eingangsrechnung als Gage
/// verbucht, steht nur hier.
/// </remarks>
public class TouringService(SouffleurDbContext context)
{
    public const int MaxResults = 500;

    private IQueryable<Tour> Query() => context.Tours
        .Include(t => t.Production)
        .Include(t => t.Members).ThenInclude(m => m.Contact)
        .Include(t => t.Members).ThenInclude(m => m.Function)
        .Include(t => t.Stops).ThenInclude(s => s.Venue)
        .Include(t => t.Stops).ThenInclude(s => s.ContactPerson)
        .Include(t => t.Stops).ThenInclude(s => s.Voucher)
        .Include(t => t.Stops).ThenInclude(s => s.Performances);

    /// <summary>
    /// Alle Tourneen, die Juengste zuerst -- gemessen an ihren Gastspielen,
    /// nicht am Anlagedatum: Was als Naechstes anreist, steht oben.
    /// </summary>
    public async Task<List<Tour>> ListAsync(bool includeSettled = true, CancellationToken token = default)
    {
        List<Tour> tours = await Query().AsNoTracking().Take(MaxResults).ToListAsync(token);

        if (!includeSettled)
        {
            tours = [.. tours.Where(t => t.Stops.Any(s => !s.Status.IsBinding() || s.Status == TourStopStatus.Confirmed)
                                       || t.Stops.Count == 0)];
        }

        foreach (Tour tour in tours)
        {
            Arrange(tour);
        }

        return [.. tours
            .OrderBy(t => t.Span is null)
            .ThenByDescending(t => t.Span?.From)
            .ThenBy(t => t.Title)];
    }

    public async Task<Tour?> FindAsync(Guid id, CancellationToken token = default)
    {
        Tour? tour = await Query().AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, token);
        if (tour is not null)
        {
            Arrange(tour);
        }

        return tour;
    }

    public async Task<TourStop?> FindStopAsync(Guid id, CancellationToken token = default)
    {
        TourStop? stop = await context.TourStops
            .Include(s => s.Tour)
            .Include(s => s.Venue)
            .Include(s => s.ContactPerson)
            .Include(s => s.Voucher)
            .Include(s => s.Performances)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, token);

        if (stop is not null)
        {
            stop.Performances = [.. stop.Performances.OrderBy(p => p.StartsAt)];
        }

        return stop;
    }

    // --- Tourneen ---

    public async Task<Tour> AddAsync(
        Tour tour, IReadOnlyCollection<MemberInput>? members = null, CancellationToken token = default)
    {
        tour.Members = await MembersFromAsync(members ?? [], token);
        await EnsureValidAsync(tour, token);

        context.Tours.Add(tour);
        await context.SaveChangesAsync(token);
        return tour;
    }

    public async Task<Tour> UpdateAsync(
        Guid id,
        Action<Tour> change,
        IReadOnlyCollection<MemberInput>? members = null,
        CancellationToken token = default)
    {
        Tour tour = await context.Tours
            .Include(t => t.Members)
            .FirstOrDefaultAsync(t => t.Id == id, token)
            ?? throw new TouringException("Diese Tournee gibt es nicht.");

        change(tour);

        if (members is not null)
        {
            // Die Reisenden werden als Ganzes ersetzt. Neue Zeilen muessen
            // ausdruecklich hinzugefuegt werden -- an einem geladenen Objekt
            // macht EF sonst ein UPDATE auf eine Zeile, die es nicht gibt.
            context.TourMembers.RemoveRange(tour.Members);
            tour.Members = await MembersFromAsync(members, token);
            foreach (TourMember member in tour.Members)
            {
                member.TourId = tour.Id;
                context.TourMembers.Add(member);
            }
        }

        await EnsureValidAsync(tour, token);
        await context.SaveChangesAsync(token);
        return tour;
    }

    public async Task DeleteAsync(Guid id, CancellationToken token = default)
    {
        Tour tour = await context.Tours
            .Include(t => t.Stops)
            .FirstOrDefaultAsync(t => t.Id == id, token)
            ?? throw new TouringException("Diese Tournee gibt es nicht.");

        // Abgerechnetes haengt an Belegen. Was in der Buchhaltung steht, wird
        // nicht aus dem Touring geloescht -- hoechstens die Tournee bleibt als
        // Erinnerung stehen.
        if (tour.Stops.Any(s => s.Status == TourStopStatus.Settled || s.VoucherId is not null))
        {
            throw new TouringException(
                "Die Tournee hat abgerechnete Gastspiele oder Belege und bleibt deshalb stehen.");
        }

        context.Tours.Remove(tour);
        await context.SaveChangesAsync(token);
    }

    // --- Gastspiele ---

    public async Task<TourStop> AddStopAsync(
        Guid tourId,
        TourStop stop,
        IReadOnlyCollection<PerformanceInput>? performances = null,
        CancellationToken token = default)
    {
        if (!await context.Tours.AnyAsync(t => t.Id == tourId, token))
        {
            throw new TouringException("Diese Tournee gibt es nicht.");
        }

        stop.TourId = tourId;
        stop.Performances = [.. (performances ?? []).Select(ToPerformance)];
        await EnsureValidAsync(stop, token);

        context.TourStops.Add(stop);
        await context.SaveChangesAsync(token);
        return stop;
    }

    public async Task<TourStop> UpdateStopAsync(
        Guid id,
        Action<TourStop> change,
        IReadOnlyCollection<PerformanceInput>? performances = null,
        CancellationToken token = default)
    {
        TourStop stop = await context.TourStops
            .Include(s => s.Performances)
            .FirstOrDefaultAsync(s => s.Id == id, token)
            ?? throw new TouringException("Dieses Gastspiel gibt es nicht.");

        change(stop);

        if (performances is not null)
        {
            context.TourPerformances.RemoveRange(stop.Performances);
            stop.Performances = [.. performances.Select(ToPerformance)];
            foreach (TourPerformance performance in stop.Performances)
            {
                performance.StopId = stop.Id;
                context.TourPerformances.Add(performance);
            }
        }

        await EnsureValidAsync(stop, token);
        await context.SaveChangesAsync(token);
        return stop;
    }

    public async Task DeleteStopAsync(Guid id, CancellationToken token = default)
    {
        TourStop stop = await context.TourStops.FirstOrDefaultAsync(s => s.Id == id, token)
            ?? throw new TouringException("Dieses Gastspiel gibt es nicht.");

        if (stop.Status == TourStopStatus.Settled || stop.VoucherId is not null)
        {
            throw new TouringException(
                "Das Gastspiel ist abgerechnet oder hat einen Beleg und bleibt deshalb stehen.");
        }

        context.TourStops.Remove(stop);
        await context.SaveChangesAsync(token);
    }

    // --- Pruefungen ---

    private async Task EnsureValidAsync(Tour tour, CancellationToken token)
    {
        List<string> problems = [.. tour.Validate()];

        if (tour.ProductionId is { } production
            && !await context.Productions.AnyAsync(p => p.Id == production, token))
        {
            problems.Add("Diese Produktion gibt es nicht.");
        }

        if (problems.Count > 0)
        {
            throw new TouringException(problems);
        }
    }

    private async Task EnsureValidAsync(TourStop stop, CancellationToken token)
    {
        List<string> problems = [.. stop.Validate()];

        Contact? venue = await context.Contacts
            .Include(c => c.Roles)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == stop.VenueId, token);

        if (venue is null)
        {
            problems.Add("Die Spielstätte steht nicht im Adressbuch.");
        }
        else if (!venue.HasRole(ContactRole.Venue))
        {
            // Nicht stillschweigend durchlassen: Wer die Druckerei als
            // Spielstaette waehlt, hat sich vertan -- und die Rolle ist das,
            // worueber das Adressbuch spaeter „alle Spielstaetten" findet.
            problems.Add($"„{venue.Name}“ hat im Adressbuch nicht die Rolle Spielstätte.");
        }

        if (stop.ContactPersonId is { } person
            && !await context.Contacts.AnyAsync(c => c.Id == person, token))
        {
            problems.Add("Die Ansprechperson steht nicht im Adressbuch.");
        }

        if (stop.VoucherId is { } voucherId)
        {
            Voucher? voucher = await context.Vouchers.AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == voucherId, token);

            if (voucher is null)
            {
                problems.Add("Den Beleg gibt es nicht.");
            }
            else if (voucher.Direction != VoucherDirection.Outgoing)
            {
                problems.Add($"Beleg {voucher.Label} ist eine Eingangsrechnung -- eine Gage ist ein Ausgangsbeleg.");
            }
        }

        if (problems.Count > 0)
        {
            throw new TouringException(problems);
        }
    }

    private async Task<List<TourMember>> MembersFromAsync(
        IReadOnlyCollection<MemberInput> inputs, CancellationToken token)
    {
        List<Guid> ids = [.. inputs.Select(i => i.ContactId).Distinct()];
        int known = await context.Contacts.CountAsync(c => ids.Contains(c.Id), token);
        if (known != ids.Count)
        {
            throw new TouringException("Eine der Personen steht nicht im Adressbuch.");
        }

        List<Guid> functions = [.. inputs.Where(i => i.FunctionId != null).Select(i => i.FunctionId!.Value).Distinct()];
        int knownFunctions = await context.CatalogEntries.CountAsync(
            e => functions.Contains(e.Id) && e.Catalog == CatalogKind.ParticipationRole, token);
        if (knownFunctions != functions.Count)
        {
            throw new TouringException("Eine der Funktionen steht nicht in der Liste der Funktionen am Termin.");
        }

        return [.. inputs.Select(i => new TourMember
        {
            ContactId = i.ContactId,
            FunctionId = i.FunctionId,
            Status = i.Status,
            Note = i.Note,
        })];
    }

    private static TourPerformance ToPerformance(PerformanceInput input) => new()
    {
        StartsAt = input.StartsAt,
        EndsAt = input.EndsAt,
        Note = input.Note,
    };

    /// <summary>Gastspiele nach Anreise, Vorstellungen nach Beginn, Reisende nach Name.</summary>
    private static void Arrange(Tour tour)
    {
        tour.Stops = [.. tour.Stops
            .OrderBy(s => s.ArrivalDate ?? s.Performances.Select(p => DateOnly.FromDateTime(p.StartsAt.UtcDateTime)).DefaultIfEmpty(DateOnly.MaxValue).Min())
            .ThenBy(s => s.Venue?.Name)];

        foreach (TourStop stop in tour.Stops)
        {
            stop.Performances = [.. stop.Performances.OrderBy(p => p.StartsAt)];
        }

        tour.Members = [.. tour.Members.OrderBy(m => m.Contact?.Name)];
    }
}
