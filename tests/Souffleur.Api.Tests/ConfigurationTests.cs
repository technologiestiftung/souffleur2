using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Souffleur.Infrastructure;

namespace Souffleur.Api.Tests;

/// <summary>
/// Hält fest, dass die Anwendung ihren Datenbankpfad zur Laufzeit aus der
/// Konfiguration nimmt.
/// </summary>
/// <remarks>
/// Hintergrund: Zuerst wurde der Pfad schon beim Registrieren der Dienste
/// eingefroren. Konfiguration, die später dazukam, lief damit ins Leere -- alle
/// Tests schrieben in dieselbe Standarddatenbank und fielen sich gegenseitig ins
/// Wort. Derselbe Fehler träfe im Betrieb jede Einstellung aus einer
/// Umgebungsvariablen.
/// </remarks>
public class ConfigurationTests(SouffleurApiFactory factory) : IClassFixture<SouffleurApiFactory>
{
    [Fact]
    public void Die_Datenbank_liegt_im_konfigurierten_Verzeichnis()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IConfiguration configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        string path = DependencyInjection.DatabasePath(configuration);

        path.Should().StartWith(factory.DataDirectory);
        File.Exists(path).Should().BeTrue("der Startdienst legt sie dort an");
    }

    [Fact]
    public void Der_Standardpfad_gilt_nur_ohne_Einstellung()
    {
        IConfiguration empty = new ConfigurationBuilder().Build();

        DependencyInjection.DatabasePath(empty)
            .Should().EndWith(Path.Combine("data", "souffleur.db"));
    }
}
