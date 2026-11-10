using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Identity;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Identity;

/// <summary>
/// Was eine Person fuer sich eingestellt hat. Immer nur die eigene Ablage --
/// einen Weg zu fremden Einstellungen gibt es hier bewusst nicht.
/// </summary>
public class UserPreferenceService(SouffleurDbContext context, ICurrentUser currentUser)
{
    /// <summary>
    /// Mehr braucht niemand, und eine Grenze verhindert, dass ein fehlerhafter
    /// Client die Tabelle vollschreibt.
    /// </summary>
    private const int Hoechstzahl = 200;

    public async Task<IReadOnlyDictionary<string, string>> MineAsync(
        CancellationToken cancellationToken = default)
    {
        if (currentUser.Id is not { } userId)
        {
            return new Dictionary<string, string>();
        }

        return await context.UserPreferences
            .Where(p => p.UserId == userId)
            .ToDictionaryAsync(p => p.Key, p => p.Value, cancellationToken);
    }

    /// <summary>
    /// Legt an oder ueberschreibt. Gibt <c>false</c> zurueck, wenn die Grenze
    /// erreicht ist -- bestehende Schluessel lassen sich weiterhin aendern.
    /// </summary>
    public async Task<bool> SetAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        if (currentUser.Id is not { } userId)
        {
            return false;
        }

        UserPreference? vorhanden = await context.UserPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Key == key, cancellationToken);

        if (vorhanden is not null)
        {
            vorhanden.Value = value;
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }

        int bisher = await context.UserPreferences
            .CountAsync(p => p.UserId == userId, cancellationToken);

        if (bisher >= Hoechstzahl)
        {
            return false;
        }

        // Ausdruecklich hinzufuegen: Entity vergibt die Id schon im
        // Konstruktor, und ein Datensatz mit gesetztem Schluessel gilt EF als
        // vorhanden -- sonst wird daraus ein UPDATE auf eine Zeile, die es
        // nicht gibt.
        context.UserPreferences.Add(new UserPreference
        {
            UserId = userId,
            Key = key,
            Value = value,
        });

        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Zuruecksetzen heisst loeschen -- dann gilt wieder die gelieferte Reihenfolge.</summary>
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        if (currentUser.Id is not { } userId)
        {
            return;
        }

        await context.UserPreferences
            .Where(p => p.UserId == userId && p.Key == key)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
