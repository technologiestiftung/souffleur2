using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Souffleur.Domain.Modules;

namespace Souffleur.Api.Tests;

/// <summary>
/// Ein abgeschaltetes Modul muss auch serverseitig dicht sein. Wäre es nur in
/// der Oberfläche ausgeblendet, käme jeder mit einem HTTP-Aufruf weiterhin an
/// die Daten.
/// </summary>
public class ModuleGateTests(SouffleurApiFactory factory) : IClassFixture<SouffleurApiFactory>
{
    private async Task<HttpClient> SignedInAsync()
    {
        HttpClient client = factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = true });

        await client.PostAsJsonAsync("/api/account/login", new
        {
            email = SouffleurApiFactory.AdminEmail,
            password = SouffleurApiFactory.AdminPassword,
            rememberMe = false,
        });

        return client;
    }

    [Fact]
    public async Task Die_Modulliste_nennt_jedes_Modul_mit_seinem_Stand()
    {
        HttpClient client = await SignedInAsync();

        List<ModuleDto>? modules = await client.GetFromJsonAsync<List<ModuleDto>>("/api/modules");

        modules.Should().NotBeNull();

        // Gegen das Enum und nicht gegen eine Zahl: Sonst prüft der Test, dass
        // niemand ein Modul hinzufügt, statt dass die Liste alle nennt.
        modules.Should().HaveCount(SouffleurModules.All.Length);
        modules!.Select(m => m.Key).Should().BeEquivalentTo(
            SouffleurModules.All.Select(m => m.ToString()));
        modules.Should().Contain(m => m.Key == "Finance" && m.IsEnabled);
        modules.Should().Contain(m => m.Key == "Ticketing" && !m.IsEnabled)
            .Which.Label.Should().Be("Ticketing");
    }

    [Fact]
    public async Task Zwei_Module_tragen_die_anderen_und_bleiben_an()
    {
        HttpClient client = await SignedInAsync();

        List<ModuleDto>? modules = await client.GetFromJsonAsync<List<ModuleDto>>("/api/modules");

        // Ohne Übersicht gäbe es keine Startseite, ohne Kontakte keinen
        // Lieferanten an der Rechnung.
        modules!.Where(m => !m.CanBeDisabled).Select(m => m.Key)
            .Should().BeEquivalentTo("Overview", "Contacts");
    }

    [Fact]
    public async Task Kontakte_lassen_sich_nicht_abschalten()
    {
        HttpClient client = await SignedInAsync();

        HttpResponseMessage response = await client.PutAsJsonAsync(
            "/api/modules/Contacts", new { isEnabled = false, note = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Und die Schnittstelle antwortet weiterhin.
        (await client.GetAsync("/api/contacts")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Die_Uebersicht_laesst_sich_nicht_abschalten()
    {
        HttpClient client = await SignedInAsync();

        HttpResponseMessage response = await client.PutAsJsonAsync(
            "/api/modules/Overview", new { isEnabled = false, note = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Abgeschaltet_antwortet_die_Schnittstelle_nicht_mehr()
    {
        HttpClient client = await SignedInAsync();

        // Eingeschaltet erreichbar.
        (await client.GetAsync("/api/accounting/journal")).StatusCode
            .Should().Be(HttpStatusCode.OK);

        HttpResponseMessage off = await client.PutAsJsonAsync(
            "/api/modules/Finance",
            new { isEnabled = false, note = "Buchhaltung bleibt beim Steuerbüro" });
        off.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Jetzt nicht mehr -- und zwar für alle Endpunkte des Moduls.
        foreach (string path in new[]
                 {
                     "/api/accounting/journal",
                     "/api/accounting/accounts",
                     "/api/accounting/cost-centers",
                     "/api/accounting/vouchers",
                 })
        {
            HttpResponseMessage blocked = await client.GetAsync(path);
            blocked.StatusCode.Should().Be(
                HttpStatusCode.NotFound,
                "{0} gehört zu einem abgeschalteten Modul", path);
        }

        // Was nicht zum Modul gehört, bleibt erreichbar.
        (await client.GetAsync("/api/account/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Wieder an.
        await client.PutAsJsonAsync("/api/modules/Finance", new { isEnabled = true, note = (string?)null });
        (await client.GetAsync("/api/accounting/journal")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Auch_das_Schreiben_ist_gesperrt_nicht_nur_das_Lesen()
    {
        HttpClient client = await SignedInAsync();
        await client.PutAsJsonAsync("/api/modules/Finance", new { isEnabled = false, note = (string?)null });

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/accounting/accounts", new
        {
            number = "4999",
            name = "Durchgeschlüpft",
            type = 5,
            taxKey = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await client.PutAsJsonAsync("/api/modules/Finance", new { isEnabled = true, note = (string?)null });
    }

    [Fact]
    public async Task Touring_ist_in_einer_frischen_Installation_aus_und_dann_dicht()
    {
        HttpClient client = await SignedInAsync();

        List<ModuleDto>? modules = await client.GetFromJsonAsync<List<ModuleDto>>("/api/modules");
        modules.Should().Contain(m => m.Key == "Touring" && !m.IsEnabled && m.CanBeDisabled)
            .Which.Label.Should().Be("Touring");

        // Aus heißt: Für dieses Haus gibt es die Endpunkte nicht -- lesend wie
        // schreibend.
        (await client.GetAsync("/api/touring/tours")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync("/api/touring/tours", new { title = "Durchgeschlüpft" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Eingeschaltet antwortet die Liste -- leer, aber vorhanden.
        HttpResponseMessage on = await client.PutAsJsonAsync(
            "/api/modules/Touring", new { isEnabled = true, note = (string?)null });
        on.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage tours = await client.GetAsync("/api/touring/tours");
        tours.StatusCode.Should().Be(HttpStatusCode.OK);
        (await tours.Content.ReadFromJsonAsync<List<object>>()).Should().BeEmpty();

        await client.PutAsJsonAsync("/api/modules/Touring", new { isEnabled = false, note = (string?)null });
    }

    [Fact]
    public async Task Ohne_Anmeldung_gibt_es_die_Modulliste_nicht()
    {
        HttpResponseMessage response = await factory.CreateClient().GetAsync("/api/modules");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private record ModuleDto(int Module, string Key, string Label, bool IsEnabled, bool CanBeDisabled, string? Note);
}
