using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Souffleur.Domain.Identity;
using Souffleur.Infrastructure.Accounting;
using Souffleur.Infrastructure.Accounting.Datev;
using Souffleur.Infrastructure.Banking;
using Souffleur.Infrastructure.Bookmarks;
using Souffleur.Infrastructure.Calendar;
using Souffleur.Infrastructure.Catalogs;
using Souffleur.Infrastructure.Contacts;
using Souffleur.Infrastructure.Conversations;
using Souffleur.Infrastructure.Modules;
using Souffleur.Infrastructure.Persistence;
using Souffleur.Infrastructure.Personnel;
using Souffleur.Infrastructure.Productions;
using Souffleur.Infrastructure.Resources;
using Souffleur.Infrastructure.Ticketing;
using Souffleur.Infrastructure.Touring;
using Souffleur.Infrastructure.Work;
using Souffleur.Infrastructure.Identity;
using Souffleur.Infrastructure.Documents;
using Souffleur.Infrastructure.Notifications;
using Souffleur.Infrastructure.Mail;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Souffleur.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSouffleurInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<SqlitePragmaInterceptor>();

        services.AddDbContext<SouffleurDbContext>((provider, options) =>
        {
            // Den Pfad erst hier aufloesen, nicht schon beim Registrieren: Konfiguration,
            // die spaeter dazukommt -- Umgebungsvariablen, Testeinstellungen --, wuerde
            // sonst ins Leere laufen und alles landete in derselben Standarddatenbank.
            string databasePath = DatabasePath(provider.GetRequiredService<IConfiguration>());

            // SQLite legt die Datei an, aber nicht das Verzeichnis darueber.
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

            options.UseSqlite($"Data Source={databasePath}")
                   .AddInterceptors(provider.GetRequiredService<SqlitePragmaInterceptor>());
        });

        services.AddIdentityCore<User>(options =>
            {
                options.Password.RequiredLength = 12;
                options.Password.RequireNonAlphanumeric = false;
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = false;
                // Fuenf Versuche, dann eine Viertelstunde Ruhe. Vorher waren
                // es zehn Versuche und fuenf Minuten -- also 120 Versuche je
                // Stunde und Konto. Das war vertretbar, solange souffleur im
                // Hausnetz stand; oeffentlich erreichbar ist es das nicht.
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<Role>()
            .AddEntityFrameworkStores<SouffleurDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders()
            // Legt die Rechte beim Anmelden in die Sitzung, damit nicht jede
            // Anfrage dafuer in die Datenbank muss.
            .AddClaimsPrincipalFactory<PermissionClaimsFactory>();

        // Zwei Minuten statt dreissig: So lange dauert es hoechstens, bis
        // eine Rechteaenderung bei der betroffenen Person ankommt.
        services.Configure<SecurityStampValidatorOptions>(
            options => options.ValidationInterval = TimeSpan.FromMinutes(2));

        services.AddScoped<AccessService>();
        services.AddScoped<AccessAdminService>();
        services.AddScoped<DocumentStorage>();
        services.AddScoped<FolderStorage>();
        services.AddScoped<FolderService>();
        services.AddScoped<DocumentService>();
        services.AddScoped<NotificationService>();
        services.TryAddScoped<INotificationPusher, NoPusher>();
        services.AddScoped<IMailSender, SmtpMailSender>();
        services.AddHostedService<NotificationMailer>();

        // Das Postfach des Hauses: senden ueber den Mailer oben, lesen per IMAP.
        services.AddSingleton<IMailbox, ImapMailbox>();
        services.AddScoped<MailService>();
        services.AddHostedService<MailboxPoller>();

        services.AddScoped<JournalService>();
        services.AddScoped<VoucherService>();
        services.AddScoped<VoucherStorage>();
        services.AddSingleton<ModuleRegistry>();
        services.AddScoped<ModuleService>();
        services.AddScoped<ContactService>();
        services.AddScoped<CatalogService>();
        services.AddScoped<CalendarService>();
        services.AddScoped<ResourceService>();
        services.AddScoped<SubscriptionService>();
        services.AddScoped<TaskService>();
        services.AddScoped<BookmarkService>();
        services.AddScoped<TimeClockService>();
        services.AddScoped<ArtistsSocialFundService>();
        services.AddScoped<DutyPlanService>();
        services.AddScoped<AbsenceService>();
        services.AddScoped<BankService>();
        services.AddScoped<ConversationService>();
        services.AddScoped<DatevExportService>();
        services.AddScoped<UserPreferenceService>();

        // Der Vorverkauf haengt an einer fremden Instanz: eigener HttpClient
        // mit kurzer Geduld, und ein Zwischenspeicher, damit nicht jeder
        // Seitenaufruf dorthin durchschlaegt.
        services.AddMemoryCache();
        services.AddHttpClient<PretixClient>(client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<TicketingService>();
        services.AddScoped<TouringService>();
        services.AddScoped<ProductionService>();
        services.AddOptions<DatevOptions>()
                .BindConfiguration(DatevOptions.Section);
        services.AddHostedService<DatabaseInitializerService>();

        // Nach dem Initializer: Das Einlesen fragt Tabellen ab, die erst die
        // Migrationen anlegen.
        services.AddHostedService<FolderScanner>();

        return services;
    }

    public static string DatabasePath(IConfiguration configuration) =>
        Path.GetFullPath(Path.Combine(
            configuration["Souffleur:DataDirectory"] ?? "data", "souffleur.db"));
}
