using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Souffleur.Domain.Identity;
using Souffleur.Infrastructure.Accounting;
using Souffleur.Infrastructure.Catalogs;
using Souffleur.Infrastructure.Modules;

namespace Souffleur.Infrastructure.Persistence;

/// <summary>
/// Bringt die Datenbank beim Start auf den aktuellen Stand und legt an, was eine
/// leere Installation braucht: die Rollen und ein erstes Administratorkonto.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IServiceProvider provider = scope.ServiceProvider;

        ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));
        var context = provider.GetRequiredService<SouffleurDbContext>();

        await context.Database.MigrateAsync(cancellationToken);

        IConfiguration configuration = provider.GetRequiredService<IConfiguration>();
        var roleManager = provider.GetRequiredService<RoleManager<Role>>();
        foreach (string name in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(name))
            {
                await roleManager.CreateAsync(new Role
                {
                    Name = name,
                    IsBuiltIn = true,
                    Description = Roles.DefaultDescription(name),
                });
                logger.LogInformation("Rolle {Role} angelegt.", name);
            }
        }

        // Rollen von vor dem Rechtesystem kennen IsBuiltIn noch nicht. Ohne
        // das Nachziehen liesse sich in einer bestehenden Installation der
        // Administrator loeschen.
        foreach (Role alt in await context.Roles
                     .Where(r => Roles.All.Contains(r.Name!) && !r.IsBuiltIn)
                     .ToListAsync(cancellationToken))
        {
            alt.IsBuiltIn = true;
            alt.Description ??= Roles.DefaultDescription(alt.Name!);
        }

        await context.SaveChangesAsync(cancellationToken);

        await SeedDefaultPermissionsAsync(context, roleManager, logger, cancellationToken);

        await SeedFirstAdministratorAsync(provider, configuration, logger);

        // Die Listen des Hauses fuellen, soweit sie leer sind -- siehe ADR-0005.
        // Das gehoert nicht zu den Beispieldaten: Auch eine Installation im
        // Betrieb braucht Terminkategorien, bevor der erste Termin entsteht.
        await CatalogSeed.RunAsync(context, logger, cancellationToken);

        // Der Modulstand liegt im Arbeitsspeicher und muss beim Start einmal
        // aus der Datenbank kommen -- sonst gelten die Vorgaben statt dem,
        // was das Haus eingestellt hat.
        await provider.GetRequiredService<ModuleRegistry>().RefreshAsync(cancellationToken);

        // Beispieldaten, damit sich die Oberfläche in der Entwicklung ansehen
        // lässt. Im Betrieb bleibt die Datenbank leer, bis das Haus sie füllt.
        if (configuration.GetValue("Souffleur:SeedDevelopmentData", false))
        {
            // Liegt im Datenverzeichnis eine haus.json, beschreibt sie das Haus
            // und seinen Spielplan. Sonst gilt der eingebaute Beispielbestand.
            HouseProfile? house = HouseProfile.Load(
                configuration.GetValue("Souffleur:DataDirectory", "data") ?? "data");

            if (house is not null)
            {
                logger.LogInformation("Hausprofil {Name} wird verwendet.", house.Name);
            }

            await DevelopmentSeed.RunAsync(
                context, provider.GetRequiredService<JournalService>(), logger, house, cancellationToken);
        }

        await SeedColleagueAsync(provider, context, configuration, logger, cancellationToken);

        await DemoHouses.SeedAccountsAsync(provider, configuration, logger);

        if (configuration.GetValue("Souffleur:SeedDevelopmentData", false))
        {
            await SeedDevelopmentGroupsAsync(provider, context, configuration, logger, cancellationToken);
        }
    }

    /// <summary>
    /// Ein paar Gruppen fuer die Entwicklung -- Technik, Inspizienz,
    /// Verwaltung -- mit den beiden Entwicklungskonten als Mitgliedern.
    /// </summary>
    /// <remarks>
    /// Nicht im <see cref="DevelopmentSeed"/>: Der laeuft, bevor das zweite
    /// Konto da ist, und beim Hauswechsel (<see cref="DemoHouses"/>) bleiben
    /// Gruppen ohnehin stehen. Hier laeuft es bei jedem Start und legt nur an,
    /// was fehlt -- wer eine Gruppe umbenennt oder leert, bekommt sie nicht
    /// zurueck.
    /// </remarks>
    private static async Task SeedDevelopmentGroupsAsync(
        IServiceProvider provider,
        SouffleurDbContext context,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var userManager = provider.GetRequiredService<UserManager<User>>();
        User? admin = await FindAsync(configuration["Souffleur:FirstAdmin:Email"]);
        User? kollege = await FindAsync(configuration["Souffleur:Colleague:Email"]);

        (string Name, string Note, User?[] Mitglieder)[] vorgaben =
        [
            ("Technik", "Bühne, Licht, Ton", [admin, kollege]),
            ("Inspizienz", "Abendspielleitung und Inspizienz", [kollege]),
            ("Verwaltung", "Büro, Buchhaltung, Leitung", [admin]),
        ];

        HashSet<string> vorhanden = await context.Groups
            .Select(g => g.Name)
            .ToHashSetAsync(cancellationToken);

        int angelegt = 0;
        foreach ((string name, string note, User?[] mitglieder) in vorgaben)
        {
            if (vorhanden.Contains(name))
            {
                continue;
            }

            var gruppe = new Group { Name = name, Note = note };
            foreach (User user in mitglieder.OfType<User>().DistinctBy(u => u.Id))
            {
                gruppe.Members.Add(new GroupMember { GroupId = gruppe.Id, UserId = user.Id });
            }

            context.Groups.Add(gruppe);
            angelegt++;
        }

        if (angelegt > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation("{Anzahl} Gruppen für die Entwicklung angelegt.", angelegt);
        }

        async Task<User?> FindAsync(string? email) =>
            string.IsNullOrWhiteSpace(email) ? null : await userManager.FindByEmailAsync(email);
    }

    /// <summary>
    /// Legt genau einmal ein Administratorkonto an, wenn noch keines existiert.
    /// Das Passwort kommt aus der Konfiguration und muss bei der ersten Anmeldung
    /// geaendert werden.
    /// </summary>
    private static async Task SeedFirstAdministratorAsync(
        IServiceProvider provider, IConfiguration configuration, ILogger logger)
    {
        var userManager = provider.GetRequiredService<UserManager<User>>();
        if (await userManager.Users.AnyAsync())
        {
            return;
        }

        string? email = configuration["Souffleur:FirstAdmin:Email"];
        string? password = configuration["Souffleur:FirstAdmin:Password"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "Kein Administratorkonto vorhanden und keines konfiguriert. " +
                "Souffleur__FirstAdmin__Email und Souffleur__FirstAdmin__Password setzen und neu starten.");
            return;
        }

        var admin = new User
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            GivenName = configuration["Souffleur:FirstAdmin:GivenName"] ?? "Admin",
            Surname = configuration["Souffleur:FirstAdmin:Surname"] ?? "Haus",
        };

        IdentityResult result = await userManager.CreateAsync(admin, password);
        if (!result.Succeeded)
        {
            logger.LogError(
                "Administratorkonto konnte nicht angelegt werden: {Errors}",
                string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }

        await userManager.AddToRoleAsync(admin, Roles.Administrator);
        logger.LogInformation("Administratorkonto {Email} angelegt. Passwort bitte sofort aendern.", email);
    }

    /// <summary>
    /// Gibt den mitgelieferten Rollen ihre Rechte -- einmalig, wenn eine
    /// Rolle noch gar keine hat.
    /// </summary>
    /// <remarks>
    /// Die Bedingung ist wichtig: Hat ein Haus einer Vorgaberolle Rechte
    /// weggenommen, sollen sie beim naechsten Start nicht wieder da sein.
    /// Einzige Ausnahme ist der Administrator: Er bekommt neu eingefuehrte
    /// Rechte immer, sonst erreicht ein neues Modul ein bestehendes Haus nie.
    /// Fuer die anderen Vorgaberollen traegt eine Migration nach, was ein
    /// neues Modul ihnen laut Vorgabe gibt (z. B. ProduktionsrechteNachtragen).
    /// Eine Rolle ohne jedes Recht laesst sich damit nicht herstellen -- sie
    /// wuerde bei jedem Start neu befuellt. Wer das will, loescht die Rolle
    /// oder nimmt die Leute heraus.
    /// </remarks>
    private static async Task SeedDefaultPermissionsAsync(
        SouffleurDbContext context,
        RoleManager<Role> roleManager,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        foreach ((string name, Permission[] permissions) in Roles.DefaultPermissions)
        {
            Role? role = await roleManager.FindByNameAsync(name);
            if (role is null)
            {
                continue;
            }

            bool hatSchonWelche = await context.RolePermissions
                .AnyAsync(p => p.RoleId == role.Id, cancellationToken);

            if (hatSchonWelche && name == Roles.Administrator)
            {
                // Der Administrator bekommt, was neu dazugekommen ist. Er ist
                // der, der die Rechte vergibt -- ein Recht, das er selbst nicht
                // hat, kann er niemandem geben, und ein neues Modul bliebe in
                // einem bestehenden Haus fuer alle unerreichbar.
                HashSet<Permission> vorhanden = [.. await context.RolePermissions
                    .Where(p => p.RoleId == role.Id)
                    .Select(p => p.Permission)
                    .ToListAsync(cancellationToken)];
                Permission[] fehlend = [.. permissions.Where(p => !vorhanden.Contains(p))];
                if (fehlend.Length > 0)
                {
                    context.RolePermissions.AddRange(fehlend.Select(permission => new RolePermission
                    {
                        RoleId = role.Id,
                        Permission = permission,
                    }));
                    logger.LogInformation(
                        "Rolle {Role} um {Count} neue Rechte ergaenzt.", name, fehlend.Length);
                }

                continue;
            }

            if (hatSchonWelche)
            {
                continue;
            }

            context.RolePermissions.AddRange(permissions.Select(permission => new RolePermission
            {
                RoleId = role.Id,
                Permission = permission,
            }));

            logger.LogInformation(
                "Rolle {Role} mit {Count} Rechten ausgestattet.", name, permissions.Length);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Nur in der Entwicklung: eine zweite Person, die handeln kann, damit
    /// Chat und Glocke etwas zu tun haben. Steht nur in
    /// appsettings.Development.json -- im Betrieb fehlt der Abschnitt, und es
    /// passiert nichts.
    /// </summary>
    private static async Task SeedColleagueAsync(
        IServiceProvider provider,
        SouffleurDbContext context,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        string? email = configuration["Souffleur:Colleague:Email"];
        string? password = configuration["Souffleur:Colleague:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var userManager = provider.GetRequiredService<UserManager<User>>();
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return;
        }

        var user = new User
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            GivenName = configuration["Souffleur:Colleague:GivenName"] ?? "Kollege",
            Surname = configuration["Souffleur:Colleague:Surname"] ?? "Entwicklung",
        };

        IdentityResult result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            logger.LogWarning("Zweites Entwicklungskonto ließ sich nicht anlegen: {Errors}",
                string.Join(", ", result.Errors.Select(e => e.Description)));
            return;
        }

        await userManager.AddToRoleAsync(user, Roles.Staff);

        await LinkColleagueContactAsync(provider, context, configuration, cancellationToken);

        logger.LogInformation("Zweites Entwicklungskonto {Email} angelegt.", email);
    }

    /// <summary>
    /// Haengt das zweite Entwicklungskonto an seinen Kontakt im Bestand.
    /// Laeuft beim Anlegen des Kontos und noch einmal, wenn der Bestand
    /// getauscht wird (<see cref="DemoHouses"/>) -- dann gibt es den Kontakt
    /// neu, das Konto aber schon.
    /// </summary>
    internal static async Task LinkColleagueContactAsync(
        IServiceProvider provider,
        SouffleurDbContext context,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        string? email = configuration["Souffleur:Colleague:Email"];
        string? name = configuration["Souffleur:Colleague:Contact"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var userManager = provider.GetRequiredService<UserManager<User>>();
        if (await userManager.FindByEmailAsync(email) is not { } user)
        {
            return;
        }

        var kontakt = await context.Contacts
            .FirstOrDefaultAsync(c => c.Name == name && c.UserId == null, cancellationToken);
        if (kontakt is not null)
        {
            kontakt.UserId = user.Id;
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
