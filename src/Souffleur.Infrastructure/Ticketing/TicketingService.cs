using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Souffleur.Domain.Ticketing;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Ticketing;

/// <summary>
/// Der Vorverkauf des Hauses: Einstellungen und das, was dahinter steht.
/// </summary>
/// <remarks>
/// Die Antworten werden kurz zwischengespeichert. Jeder Seitenaufruf loest
/// sonst mehrere Abfragen je Vorstellung aus, und ein gehostetes pretix hat
/// Abfragegrenzen.
/// </remarks>
public class TicketingService(
    SouffleurDbContext context,
    PretixClient pretix,
    IMemoryCache cache,
    TimeProvider timeProvider)
{
    private const string CacheKey = "ticketing:performances";

    /// <summary>
    /// Die Einstellungen. Es gibt genau einen Datensatz; fehlt er, entsteht er
    /// beim ersten Zugriff mit "kein Vorverkauf".
    /// </summary>
    public async Task<TicketingSettings> SettingsAsync(CancellationToken cancellationToken = default)
    {
        TicketingSettings? found = await context.TicketingSettings
            .OrderBy(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (found is not null)
        {
            return found;
        }

        var fresh = new TicketingSettings();
        context.TicketingSettings.Add(fresh);
        await context.SaveChangesAsync(cancellationToken);
        return fresh;
    }

    /// <summary>
    /// Nimmt die Einstellungen an und prueft gleich, ob die Verbindung steht.
    /// </summary>
    /// <param name="token">
    /// Null heisst "unveraendert" -- die Oberflaeche bekommt das hinterlegte
    /// Token nie zu sehen und kann es deshalb auch nicht zuruecksenden.
    /// </param>
    public async Task<TicketingSettings> SaveAsync(
        TicketingProvider provider,
        string? baseUrl,
        string? organizer,
        string? token,
        int cacheSeconds,
        CancellationToken cancellationToken = default)
    {
        TicketingSettings settings = await SettingsAsync(cancellationToken);

        settings.Provider = provider;
        settings.BaseUrl = Trimmed(baseUrl);
        settings.Organizer = Trimmed(organizer);
        settings.CacheSeconds = cacheSeconds;

        if (token is not null)
        {
            settings.Token = Trimmed(token);
        }

        if (provider == TicketingProvider.None)
        {
            // Kein Vorverkauf heisst: Es liegt auch kein Token mehr herum.
            settings.Token = null;
            settings.LastProblem = null;
            settings.LastCheckedAt = null;
        }

        IReadOnlyList<string> problems = settings.Validate();
        if (problems.Count > 0)
        {
            throw new TicketingException(problems);
        }

        if (settings.IsConfigured)
        {
            settings.LastProblem = await pretix.CheckAsync(settings, cancellationToken);
            settings.LastCheckedAt = timeProvider.GetUtcNow();
        }

        await context.SaveChangesAsync(cancellationToken);
        cache.Remove(CacheKey);

        return settings;
    }

    /// <summary>Prueft die Verbindung noch einmal, ohne etwas zu aendern.</summary>
    public async Task<TicketingSettings> CheckAsync(CancellationToken cancellationToken = default)
    {
        TicketingSettings settings = await SettingsAsync(cancellationToken);

        if (!settings.IsConfigured)
        {
            throw new TicketingException(["Der Vorverkauf ist noch nicht eingerichtet."]);
        }

        settings.LastProblem = await pretix.CheckAsync(settings, cancellationToken);
        settings.LastCheckedAt = timeProvider.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);
        cache.Remove(CacheKey);

        return settings;
    }

    /// <summary>
    /// Die Vorstellungen mit ihren Verkaufszahlen. Ohne eingerichteten
    /// Vorverkauf eine leere Liste -- das ist kein Fehler, sondern der
    /// Normalfall fuer ein Haus ohne eigenen Kartenverkauf.
    /// </summary>
    public async Task<IReadOnlyList<Performance>> PerformancesAsync(
        CancellationToken cancellationToken = default)
    {
        TicketingSettings settings = await SettingsAsync(cancellationToken);

        if (!settings.IsConfigured)
        {
            return [];
        }

        if (cache.TryGetValue(CacheKey, out IReadOnlyList<Performance>? cached) && cached is not null)
        {
            return cached;
        }

        IReadOnlyList<Performance> found = await pretix.PerformancesAsync(settings, cancellationToken);

        if (settings.CacheSeconds > 0)
        {
            cache.Set(CacheKey, found, TimeSpan.FromSeconds(settings.CacheSeconds));
        }

        return found;
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public class TicketingException(IReadOnlyList<string> problems)
    : InvalidOperationException(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}
