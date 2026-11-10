using Microsoft.AspNetCore.DataProtection;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Souffleur.Api.Features.Accounting;
using Souffleur.Api.Features.Authentication;
using Souffleur.Api.Features.Banking;
using Souffleur.Api.Features.Development;
using Souffleur.Api.Features.Calendar;
using Souffleur.Api.Features.Catalogs;
using Souffleur.Api.Features.Chat;
using Souffleur.Api.Features.Contacts;
using Souffleur.Api.Features.Modules;
using Souffleur.Api.Features.Organization;
using Souffleur.Api.Features.Personnel;
using Souffleur.Api.Features.Resources;
using Souffleur.Api.Features.Ticketing;
using Souffleur.Api.Features.Touring;
using Souffleur.Api.Features.Bookmarks;
using Souffleur.Api.Features.Work;
using Souffleur.Api.Infrastructure;
using Souffleur.Infrastructure;
using Souffleur.Infrastructure.Persistence;
using Souffleur.Api.Features.Preferences;
using Souffleur.Api.Features.Productions;
using Souffleur.Api.Features.Access;
using Souffleur.Api.Features.Documents;
using Souffleur.Api.Features.Notifications;
using Souffleur.Api.Features.Search;
using Souffleur.Api.Features.Mail;
using Souffleur.Infrastructure.Notifications;
using Microsoft.Extensions.DependencyInjection.Extensions;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddSouffleurInfrastructure();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

// Cookie statt Token: Oberflaeche und API liegen im selben Container, unter derselben
// Herkunft. Das spart die Token-Erneuerung und laesst das Konto serverseitig sperren.
builder.Services
    .AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();
// Hinter einem Reverse Proxy kommt die Anfrage als HTTP an, auch wenn der
// Browser HTTPS gesprochen hat. Ohne diese Zeilen haelt die Anwendung die
// Verbindung fuer unverschluesselt, setzt das Cookie ohne Secure-Flag, und
// der Browser schickt es anschliessend auch ueber HTTP.
//
// Die Header werden von jedem Absender angenommen. Das ist vertretbar,
// solange der Port nur lokal gebunden ist (so die Vorgabe in
// deploy/haus/docker-compose.yml) -- dann kommt ohnehin nur der Proxy
// heran. Steht der Container offen im Netz, gehoeren hier die Adressen
// der eigenen Proxys hinein.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Die Schluessel, mit denen das Anmeldecookie verschluesselt ist, gehoeren
// ins Datenverzeichnis. Ohne das lagen sie nur im Arbeitsspeicher, und jeder
// Neustart -- auch das naechtliche Update im Haus -- meldete alle ab; ein
// halb ausgefuellter Bogen scheiterte danach mit 401.
builder.Services.AddDataProtection()
    .SetApplicationName("souffleur")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(
        builder.Configuration.GetValue("Souffleur:DataDirectory", "data") ?? "data", "keys")));

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "souffleur";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;

    // Nur ueber verschluesselte Verbindungen. Eine Installation, die im
    // Hausnetz bewusst ohne TLS laeuft, setzt Souffleur:RequireHttps auf
    // false -- und weiss dann, dass die Anmeldung im Klartext durchs
    // Netz geht.
    options.Cookie.SecurePolicy = builder.Configuration.GetValue("Souffleur:RequireHttps", true)
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.SameAsRequest;

    // Vierzehn Tage mit Verlaengerung: Wer taeglich damit arbeitet, meldet
    // sich nie neu an. Das ist eine Abwaegung zugunsten der Benutzbarkeit;
    // ein gesperrtes Konto wirkt serverseitig trotzdem sofort.
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;

    // Ohne das antwortet die API auf einen abgelaufenen Zugang mit einer
    // Weiterleitung zur Anmeldeseite -- die Oberflaeche braucht den Statuscode.
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});
builder.Services.AddAccountingAuthorization();

// Die Kontosperre greift je Konto -- fuenf Fehlversuche, dann eine
// Viertelstunde Ruhe. Das ist der Schutz gegen das Raten eines Passworts.
// Diese Grenze hier ist etwas anderes: Sie soll die Flut abfangen, also
// jemanden, der Adressen durchprobiert oder den Endpunkt zumacht.
//
// Deshalb ist sie bewusst grosszuegig. Ein Haus sitzt hinter EINER
// oeffentlichen Adresse: Melden sich morgens zwanzig Leute an, kommen alle
// vom selben Absender. Eine zu enge Grenze sperrt dann die Belegschaft aus
// und nicht den Angreifer -- ein Fehler, der im Betrieb teurer waere als
// die Luecke, die er schliessen soll.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("anmeldung", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unbekannt",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("Souffleur:LoginAttemptsPerWindow", 50),
            Window = TimeSpan.FromMinutes(5),
        }));
});

builder.Services.AddSignalR();

// Die Glocke klingelt ueber SignalR. Die Infrastruktur traegt nur einen
// stummen Zusteller ein; der echte sitzt hier, weil nur das API-Projekt
// den Hub kennt.
builder.Services.Replace(ServiceDescriptor.Scoped<INotificationPusher, SignalRNotificationPusher>());
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

WebApplication app = builder.Build();

// Muss vor allem anderen stehen, was Schema oder Adresse auswertet.
app.UseForwardedHeaders();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthenticationEndpoints();
app.MapAccountingMasterDataEndpoints();
app.MapJournalEndpoints();
app.MapVoucherEndpoints();
app.MapDatevEndpoints();
app.MapBankEndpoints();
app.MapCalendarEndpoints();
app.MapSubscriptionEndpoints();
app.MapCatalogEndpoints();
app.MapChatEndpoints();
app.MapContactEndpoints();
app.MapModuleEndpoints();
app.MapPreferenceEndpoints();
app.MapAccessEndpoints();
app.MapDocumentEndpoints();
app.MapFolderEndpoints();
app.MapNotificationEndpoints();
app.MapOrganizationEndpoints();
app.MapResourceEndpoints();
app.MapTaskEndpoints();
app.MapBookmarkEndpoints();
app.MapTimeEndpoints();
app.MapArtistsSocialFundEndpoints();
app.MapDutyPlanEndpoints();
app.MapAbsenceEndpoints();
app.MapTicketingEndpoints();
app.MapTouringEndpoints();
app.MapProductionEndpoints();
app.MapSearchEndpoints();
app.MapMailEndpoints();

// Die Vorfuehrdaten im laufenden Betrieb gegen ein anderes Haus tauschen:
// in der Entwicklung immer, im Betrieb nur auf einer ausdruecklichen
// Vorfuehrinstanz (Souffleur:HouseSwitching), die mehrere Haeuser zeigen
// soll, ohne dass man sich neu anmeldet. Ein echtes Haus setzt den Schalter
// nie -- es wuerde seinen Bestand mit einem Klick verlieren koennen.
if (app.Environment.IsDevelopment()
    || app.Configuration.GetValue(DemoHouses.SwitchingKey, false))
{
    app.MapDevelopmentEndpoints();
}
app.MapHealthChecks("/api/health");

// Jeder Pfad, der nicht die API ist, gehoert der Angular-Anwendung.
app.MapFallbackToFile("index.html");

// Ohne Zeitzonendatenbank laeuft souffleur an, scheitert aber am ersten
// Termin: Ein schlanker Container bringt oft keine mit. Lieber hier
// abbrechen, mit einem Satz, der sagt was fehlt, als spaeter im Kalender.
try
{
    TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
}
catch (TimeZoneNotFoundException problem)
{
    throw new InvalidOperationException(
        "Auf diesem System fehlt die Zeitzonendatenbank -- souffleur rechnet "
        + "jeden Termin damit. In einem Alpine-Container hilft 'apk add tzdata'.",
        problem);
}

app.Run();

/// <summary>Sichtbar fuer die Integrationstests (WebApplicationFactory).</summary>
public partial class Program;
