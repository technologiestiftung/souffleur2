using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Souffleur.Domain.Catalogs;
using Souffleur.Domain.Identity;
using Souffleur.Domain.Modules;
using Souffleur.Domain.Personnel;
using Souffleur.Domain.Ticketing;
using Souffleur.Infrastructure.Accounting;

namespace Souffleur.Infrastructure.Persistence;

/// <summary>
/// Die Vorfuehrdaten im laufenden Betrieb gegen die eines anderen Hauses
/// tauschen -- nur in der Entwicklung.
/// </summary>
/// <remarks>
/// Warum nicht die Datenbank loeschen und neu starten: Wer vorfuehrt, will
/// sich einmal anmelden und dann zwischen den Haeusern wechseln, ohne dass
/// Konten, Rechte, Gruppen und die eigene Reiterreihenfolge mitgehen. Deshalb
/// bleibt alles, was zu Personen und Einstellungen gehoert; nur der Bestand
/// des Hauses wird geleert und aus dem Profil neu angelegt.
///
/// Die Profile liegen ausserhalb des Repositorys (Kundendaten, siehe
/// <see cref="HouseProfile"/>). Das Verzeichnis nennt die Konfiguration unter
/// <see cref="DirectoryKey"/>; fehlt es, gibt es nichts zu wechseln.
/// </remarks>
public static partial class DemoHouses
{
    public const string DirectoryKey = "Souffleur:HouseProfilesDirectory";

    public sealed record AvailableHouse(string Key, string Name, bool IsCurrent);

    /// <summary>
    /// Was bleibt: Personen, Rollen, Rechte, Gruppen, persoenliche
    /// Einstellungen, Modulschalter, Kataloge, KSK-Saetze, Ticketing-Zugang.
    /// Alles andere gehoert zum Bestand des Hauses und wird ersetzt.
    /// </summary>
    private static readonly HashSet<Type> Kept =
    [
        typeof(User),
        typeof(Role),
        typeof(IdentityUserRole<Guid>),
        typeof(IdentityUserClaim<Guid>),
        typeof(IdentityUserLogin<Guid>),
        typeof(IdentityUserToken<Guid>),
        typeof(IdentityRoleClaim<Guid>),
        typeof(RolePermission),
        typeof(Souffleur.Domain.Identity.Group),
        typeof(GroupMember),
        typeof(GroupRole),
        typeof(UserPreference),
        typeof(ModuleSetting),
        typeof(CatalogEntry),
        typeof(ArtistsSocialFundRate),
        typeof(TicketingSettings),
    ];

    /// <summary>
    /// Auf der Vorfuehrinstanz gehoeren auch die Gruppen zum Haus: Was ein Haus
    /// als Gruppe anlegt, soll das naechste nach dem naechsten Wechsel nicht
    /// sehen. In der Entwicklung bleiben sie, damit der Dienstplan etwas
    /// vorzuschlagen hat (SeedDevelopmentGroupsAsync).
    /// </summary>
    private static readonly HashSet<Type> HouseOwnedOnDemo =
    [
        typeof(Souffleur.Domain.Identity.Group),
        typeof(GroupMember),
        typeof(GroupRole),
    ];

    /// <summary>Die Haeuser, die zur Wahl stehen -- das laufende markiert.</summary>
    public static IReadOnlyList<AvailableHouse> List(IConfiguration configuration, string? currentName)
    {
        string? directory = configuration[DirectoryKey];
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        var houses = new List<AvailableHouse>();

        foreach (string path in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            string key = Path.GetFileNameWithoutExtension(path);
            if (!KeyPattern().IsMatch(key))
            {
                continue;
            }

            // Eine kaputte Datei soll die Liste nicht mitreissen -- sie faellt
            // weg und wird gemeldet, sobald jemand sie waehlt.
            HouseProfile? profile;
            try
            {
                profile = HouseProfile.LoadFile(path);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            if (profile is not null)
            {
                houses.Add(new AvailableHouse(
                    key, profile.Name, string.Equals(profile.Name, currentName, StringComparison.Ordinal)));
            }
        }

        return houses;
    }

    /// <summary>
    /// Leert den Bestand des Hauses und legt ihn aus dem gewaehlten Profil neu
    /// an. <c>null</c>, wenn es kein Profil mit diesem Schluessel gibt.
    /// </summary>
    public static async Task<HouseProfile?> SwitchAsync(
        IServiceProvider provider, string key, CancellationToken cancellationToken = default)
    {
        IConfiguration configuration = provider.GetRequiredService<IConfiguration>();
        string? directory = configuration[DirectoryKey];

        if (string.IsNullOrWhiteSpace(directory) || !KeyPattern().IsMatch(key))
        {
            return null;
        }

        string path = Path.Combine(directory, key + ".json");
        if (HouseProfile.LoadFile(path) is not { } profile)
        {
            return null;
        }

        var context = provider.GetRequiredService<SouffleurDbContext>();
        ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DemoHouses));

        logger.LogInformation("Vorfuehrdaten werden gegen {Name} getauscht.", profile.Name);

        await ClearHouseDataAsync(context, configuration.GetValue(SwitchingKey, false), cancellationToken);

        await DevelopmentSeed.RunAsync(
            context, provider.GetRequiredService<JournalService>(), logger, profile, cancellationToken);

        // Das zweite Entwicklungskonto haengt an einem Kontakt des Bestands;
        // der ist gerade neu entstanden und kennt das Konto noch nicht.
        await DatabaseInitializer.LinkColleagueContactAsync(provider, context, configuration, cancellationToken);

        // Das Datenverzeichnis soll zeigen, was laeuft: Wer die Datenbank
        // spaeter loescht, bekommt beim naechsten Start dasselbe Haus.
        string dataDirectory = configuration.GetValue("Souffleur:DataDirectory", "data") ?? "data";
        Directory.CreateDirectory(dataDirectory);
        File.Copy(path, Path.Combine(dataDirectory, HouseProfile.FileName), overwrite: true);

        return profile;
    }

    /// <summary>Schalter der Vorfuehrinstanz, siehe Program.cs.</summary>
    public const string SwitchingKey = "Souffleur:HouseSwitching";

    /// <summary>
    /// Konten, die an ein Haus gebunden sind: <c>Souffleur:DemoAccounts:n:Email</c>,
    /// <c>Password</c>, <c>GivenName</c>, <c>Surname</c>, <c>House</c> (Schluessel
    /// des Profils, etwa <c>kanal</c>).
    /// </summary>
    public const string AccountsKey = "Souffleur:DemoAccounts";

    private sealed record DemoAccount(string Email, string? Password, string? GivenName, string? Surname, string House);

    private static IEnumerable<DemoAccount> Accounts(IConfiguration configuration) =>
        configuration.GetSection(AccountsKey).GetChildren()
            .Select(s => new DemoAccount(
                s["Email"] ?? string.Empty, s["Password"], s["GivenName"], s["Surname"], s["House"] ?? string.Empty))
            .Where(a => !string.IsNullOrWhiteSpace(a.Email) && KeyPattern().IsMatch(a.House));

    /// <summary>
    /// Legt die Konten der Vorfuehrinstanz an, soweit sie fehlen -- als
    /// Administration, damit jede Person ihr Haus ganz zeigen kann.
    /// </summary>
    /// <remarks>
    /// Konten anlegen hat noch keine Oberflaeche und keinen Endpunkt; fuer die
    /// Vorfuehrung bei den Haeusern braucht
    /// aber jedes Haus seine Anmeldung. Nur mit dem Schalter, nie im Haus.
    /// </remarks>
    public static async Task SeedAccountsAsync(IServiceProvider provider, IConfiguration configuration, ILogger logger)
    {
        if (!configuration.GetValue(SwitchingKey, false))
        {
            return;
        }

        var userManager = provider.GetRequiredService<UserManager<User>>();

        foreach (DemoAccount account in Accounts(configuration))
        {
            if (string.IsNullOrWhiteSpace(account.Password)
                || await userManager.FindByEmailAsync(account.Email) is not null)
            {
                continue;
            }

            var user = new User
            {
                UserName = account.Email,
                Email = account.Email,
                EmailConfirmed = true,
                GivenName = account.GivenName ?? "Vorführung",
                Surname = account.Surname ?? account.House,
            };

            IdentityResult result = await userManager.CreateAsync(user, account.Password);
            if (!result.Succeeded)
            {
                logger.LogError(
                    "Vorfuehrkonto {Email} nicht angelegt: {Errors}",
                    account.Email, string.Join("; ", result.Errors.Select(e => e.Description)));
                continue;
            }

            await userManager.AddToRoleAsync(user, Roles.Administrator);
            logger.LogInformation("Vorfuehrkonto {Email} fuer {House} angelegt.", account.Email, account.House);
        }
    }

    /// <summary>
    /// Nach der Anmeldung: Gehoert das Konto zu einem Haus und steht gerade ein
    /// anderes, wird getauscht. Wer sich als Kanal anmeldet, sieht Kanal.
    /// </summary>
    /// <remarks>
    /// Eine Datenbank fuer alle: Wer zur selben Zeit in einem anderen Haus
    /// angemeldet ist, sieht nach dem Neuladen das neue. Fuer eine
    /// Vorfuehrinstanz in Kauf genommen -- echte Haeuser haben je eine Instanz.
    /// </remarks>
    public static async Task SwitchForAccountAsync(
        IServiceProvider provider, string email, CancellationToken cancellationToken = default)
    {
        IConfiguration configuration = provider.GetRequiredService<IConfiguration>();
        if (!configuration.GetValue(SwitchingKey, false))
        {
            return;
        }

        DemoAccount? account = Accounts(configuration)
            .FirstOrDefault(a => string.Equals(a.Email, email, StringComparison.OrdinalIgnoreCase));
        if (account is null)
        {
            return;
        }

        var context = provider.GetRequiredService<SouffleurDbContext>();
        string? current = await context.Organizations.AsNoTracking()
            .Select(o => o.Name)
            .FirstOrDefaultAsync(cancellationToken);

        bool schonDa = List(configuration, current)
            .Any(h => h.Key == account.House && h.IsCurrent);
        if (!schonDa)
        {
            await SwitchAsync(provider, account.House, cancellationToken);
        }
    }

    /// <summary>
    /// Ob das Konto an ein Haus gebunden ist. Solche Konten duerfen nicht
    /// wechseln und nicht einmal sehen, welche Haeuser es sonst gibt: Die
    /// Ensembles sollen voneinander nichts wissen (Wolf, 09.11.2026).
    /// </summary>
    public static bool IsBoundAccount(IConfiguration configuration, string? email) =>
        !string.IsNullOrWhiteSpace(email)
        && Accounts(configuration).Any(a => string.Equals(a.Email, email, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Die Konten der anderen Haeuser, die ein Vorfuehrkonto nicht sehen soll.
    /// </summary>
    /// <remarks>
    /// Konten bleiben beim Tausch stehen (<see cref="Kept"/>). Ohne diesen
    /// Filter saehe ein Haus unter Zugaenge und im Chat die Konten der
    /// anderen Haeuser. Wer kein Vorfuehrkonto ist, sieht alle.
    /// </remarks>
    public static async Task<HashSet<Guid>> HiddenAccountsAsync(
        SouffleurDbContext context, IConfiguration configuration, string? viewerEmail,
        CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue(SwitchingKey, false) || string.IsNullOrWhiteSpace(viewerEmail))
        {
            return [];
        }

        List<DemoAccount> accounts = [.. Accounts(configuration)];
        DemoAccount? viewer = accounts
            .FirstOrDefault(a => string.Equals(a.Email, viewerEmail, StringComparison.OrdinalIgnoreCase));
        if (viewer is null)
        {
            return [];
        }

        List<string> fremde = [.. accounts
            .Where(a => a.House != viewer.House)
            .Select(a => a.Email.ToUpperInvariant())];

        return [.. await context.Users
            .Where(u => u.NormalizedEmail != null && fremde.Contains(u.NormalizedEmail))
            .Select(u => u.Id)
            .ToListAsync(cancellationToken)];
    }

    private static async Task ClearHouseDataAsync(
        SouffleurDbContext context, bool demoInstance, CancellationToken cancellationToken)
    {
        // Ueber die Tabellen gehen, nicht ueber die Entitaeten: Ein Typ, der
        // seine Tabelle mit einem bleibenden teilt, darf nicht geleert werden.
        HashSet<string> keptTables = context.Model.GetEntityTypes()
            .Where(e => Kept.Contains(e.ClrType) && !(demoInstance && HouseOwnedOnDemo.Contains(e.ClrType)))
            .Select(e => e.GetTableName())
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        List<string> tables = context.Model.GetEntityTypes()
            .Select(e => e.GetTableName())
            .OfType<string>()
            .Where(t => !keptTables.Contains(t))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Fremdschluessel fuer die Dauer des Leerens aus, damit die Reihenfolge
        // der Tabellen keine Rolle spielt. Das PRAGMA gilt je Verbindung und
        // wirkt nicht in einer Transaktion -- deshalb dieselbe Verbindung
        // offen halten und jede Anweisung fuer sich ausfuehren.
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;", cancellationToken);

            foreach (string table in tables)
            {
                // Der Name kommt aus dem EF-Modell, nicht von aussen -- deshalb
                // darf er in die Anweisung; als Parameter ginge ein Tabellenname nicht.
                string statement = "DELETE FROM \"" + table.Replace("\"", "\"\"", StringComparison.Ordinal) + "\";";
#pragma warning disable EF1002, EF1003
                await context.Database.ExecuteSqlRawAsync(statement, cancellationToken);
#pragma warning restore EF1002, EF1003
            }

            await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;", cancellationToken);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }

        context.ChangeTracker.Clear();
    }

    [GeneratedRegex("^[a-z0-9-]{1,40}$")]
    private static partial Regex KeyPattern();
}
