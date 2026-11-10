using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Api.Tests;

/// <summary>
/// Die Anwendung wie in der Entwicklung: mit Vorfuehrdaten und einem
/// Verzeichnis voller Hausprofile, zwischen denen sich wechseln laesst.
/// </summary>
public class DevelopmentApiFactory : SouffleurApiFactory
{
    public string ProfilesDirectory { get; } =
        Path.Combine(Path.GetTempPath(), "souffleur-tests", "haeuser-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        Directory.CreateDirectory(ProfilesDirectory);
        File.WriteAllText(Path.Combine(ProfilesDirectory, "alpha.json"), Profil("Theater Alpha", "Alpha-Stück", "2030-03-01"));
        File.WriteAllText(Path.Combine(ProfilesDirectory, "beta.json"), Profil("Theater Beta", "Beta-Stück", "2030-04-01"));

        // Entwicklung, nicht Testing: Nur dort haengt Program.cs die Pfade ein.
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Souffleur:SeedDevelopmentData"] = "true",
                [DemoHouses.DirectoryKey] = ProfilesDirectory,
            }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(ProfilesDirectory))
        {
            Directory.Delete(ProfilesDirectory, recursive: true);
        }
    }

    private static string Profil(string name, string titel, string datum) => $$"""
        {
          "name": "{{name}}",
          "city": "Berlin",
          "resources": [ { "name": "Saal", "shortName": "S", "kind": "buehne" } ],
          "productions": [
            { "title": "{{titel}}", "resource": "S", "performances": [ { "date": "{{datum}}", "time": "20:00" } ] }
          ]
        }
        """;
}

/// <summary>
/// Wer vorfuehrt, meldet sich einmal an und wechselt dann das Haus. Dabei
/// bleiben Konto und eigene Einstellungen; der Bestand kommt neu.
/// </summary>
public class DevelopmentApiTests(DevelopmentApiFactory factory) : IClassFixture<DevelopmentApiFactory>
{
    private async Task<HttpClient> SignedInAsync()
    {
        HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        HttpResponseMessage login = await client.PostAsJsonAsync("/api/account/login", new
        {
            email = SouffleurApiFactory.AdminEmail,
            password = SouffleurApiFactory.AdminPassword,
            rememberMe = false,
        });
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        return client;
    }

    [Fact]
    public async Task Die_Entwicklung_bringt_Gruppen_mit_damit_der_Dienstplan_vorschlagen_kann()
    {
        HttpClient client = await SignedInAsync();

        List<GroupDto>? gruppen = await client.GetFromJsonAsync<List<GroupDto>>("/api/groups");

        gruppen.Should().NotBeNull();
        gruppen.Select(g => g.Name).Should().Contain(["Technik", "Inspizienz", "Verwaltung"]);
    }

    private record GroupDto(Guid Id, string Name, string? Note);

    [Fact]
    public async Task Die_Haeuser_stehen_zur_Wahl_und_das_laufende_ist_markiert()
    {
        HttpClient client = await SignedInAsync();

        List<HausDto>? haeuser = await client.GetFromJsonAsync<List<HausDto>>("/api/development/houses");

        haeuser.Should().NotBeNull();
        haeuser.Select(h => h.Key).Should().Equal("alpha", "beta");
        haeuser.Select(h => h.Name).Should().Equal("Theater Alpha", "Theater Beta");
    }

    [Fact]
    public async Task Der_Wechsel_tauscht_den_Bestand_und_laesst_die_Person_angemeldet()
    {
        HttpClient client = await SignedInAsync();

        // Etwas Eigenes, das den Wechsel ueberleben muss.
        HttpResponseMessage gemerkt = await client.PutAsJsonAsync("/api/preferences/module", new { value = "chat,finanzen" });
        gemerkt.IsSuccessStatusCode.Should().BeTrue();

        HttpResponseMessage wechsel = await client.PostAsync("/api/development/houses/beta", null);
        wechsel.StatusCode.Should().Be(HttpStatusCode.OK);

        // Angemeldet geblieben, Haus getauscht.
        (await client.GetAsync("/api/account/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement haus = await client.GetFromJsonAsync<JsonElement>("/api/organization");
        haus.GetProperty("name").GetString().Should().Be("Theater Beta");
        haus.GetProperty("hasDemoData").GetBoolean().Should().BeTrue();

        List<HausDto>? haeuser = await client.GetFromJsonAsync<List<HausDto>>("/api/development/houses");
        haeuser!.Single(h => h.IsCurrent).Key.Should().Be("beta");

        // Der Spielplan ist der des neuen Hauses -- und nur der.
        JsonElement spielplan = await client.GetFromJsonAsync<JsonElement>(
            "/api/calendar/range?from=2030-01-01&to=2030-12-31");
        List<string?> titel = spielplan.GetProperty("occurrences").EnumerateArray()
            .Select(o => o.GetProperty("title").GetString()).ToList();
        titel.Should().Contain("Beta-Stück").And.NotContain("Alpha-Stück");

        // Das Eigene ist noch da.
        Dictionary<string, string>? einstellungen =
            await client.GetFromJsonAsync<Dictionary<string, string>>("/api/preferences");
        einstellungen.Should().ContainKey("module").WhoseValue.Should().Be("chat,finanzen");

        // Und zurueck: Der alte Bestand ist wirklich weg, nicht nur ueberdeckt.
        (await client.PostAsync("/api/development/houses/alpha", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        using IServiceScope scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SouffleurDbContext>();
        (await context.Organizations.CountAsync()).Should().Be(1);
        (await context.CalendarEvents.Select(e => e.Title).ToListAsync())
            .Should().Contain("Alpha-Stück").And.NotContain("Beta-Stück");
    }

    [Fact]
    public async Task Ein_unbekanntes_Haus_gibt_es_nicht()
    {
        HttpClient client = await SignedInAsync();

        (await client.PostAsync("/api/development/houses/gibtsnicht", null)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsync("/api/development/houses/..%2Falpha", null)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    private sealed record HausDto(string Key, string Name, bool IsCurrent);
}
