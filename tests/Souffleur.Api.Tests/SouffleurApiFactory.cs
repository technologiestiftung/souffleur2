using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Souffleur.Api.Tests;

/// <summary>
/// Startet die Anwendung mit einer frischen Datenbank in einem temporaeren
/// Verzeichnis. Jede Testklasse bekommt ihre eigene.
/// </summary>
public class SouffleurApiFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "TestPasswort123";

    /// <summary>Ob das Sitzungscookie auf verschluesselte Verbindungen beschraenkt wird.</summary>
    protected virtual bool RequireHttps => false;

    /// <summary>
    /// Anmeldeversuche je Fenster und Absender. <c>null</c> laesst den Wert
    /// der Anwendung gelten -- ein Test, der die Grenze pruefen will, setzt
    /// sie niedrig und haengt damit nicht am Wert fuer den Betrieb.
    /// </summary>
    protected virtual int? LoginAttemptsPerWindow => null;

    public string DataDirectory { get; } =
        Path.Combine(Path.GetTempPath(), "souffleur-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Souffleur:DataDirectory"] = DataDirectory,
                ["Souffleur:FirstAdmin:Email"] = AdminEmail,
                ["Souffleur:FirstAdmin:Password"] = AdminPassword,
                ["Souffleur:FirstAdmin:GivenName"] = "Test",
                ["Souffleur:FirstAdmin:Surname"] = "Admin",

                // Der Testserver spricht http. Ein Cookie, das nur ueber
                // verschluesselte Verbindungen gilt, kaeme nie zurueck und
                // jeder angemeldete Test schlueg fehl. Wer das Gegenteil
                // pruefen will, leitet ab -- siehe AnmeldungSchutzTests.
                ["Souffleur:RequireHttps"] = RequireHttps ? "true" : "false",
                ["Souffleur:LoginAttemptsPerWindow"] = LoginAttemptsPerWindow?.ToString(),
            }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(DataDirectory))
        {
            Directory.Delete(DataDirectory, recursive: true);
        }
    }
}
