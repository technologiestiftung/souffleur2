using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Souffleur.Api.Tests;

/// <summary>
/// Das Haus wählt seinen Kontenrahmen selbst. souffleur liefert keinen mit --
/// was gilt, sagt das Steuerbüro.
/// </summary>
public class OrganizationApiTests(SouffleurApiFactory factory) : IClassFixture<SouffleurApiFactory>
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
    public async Task Vier_Kontenrahmen_stehen_zur_Wahl()
    {
        HttpClient client = await SignedInAsync();

        List<ChartDto>? charts = await client.GetFromJsonAsync<List<ChartDto>>(
            "/api/organization/charts-of-accounts");

        charts.Should().NotBeNull();
        charts.Select(c => c.Key).Should().BeEquivalentTo("Skr03", "Skr04", "Skr42", "Custom");

        // Nur der Vereinsrahmen verlangt die Sphärentrennung.
        charts.Single(c => c.Key == "Skr42").UsesTaxSpheres.Should().BeTrue();
        charts.Single(c => c.Key == "Skr03").UsesTaxSpheres.Should().BeFalse();
        charts.Single(c => c.Key == "Skr42").Label.Should().Contain("Vereine");
    }

    [Fact]
    public async Task Die_vier_Sphaeren_kommen_mit_Beispiel()
    {
        HttpClient client = await SignedInAsync();

        List<SphereDto>? spheres = await client.GetFromJsonAsync<List<SphereDto>>(
            "/api/organization/tax-spheres");

        spheres.Should().HaveCount(4);
        spheres.Should().Contain(s => s.Label == "Zweckbetrieb")
            .Which.Example.Should().Contain("Eintritt");
    }

    [Fact]
    public async Task Das_Haus_waehlt_seinen_Rahmen_und_souffleur_merkt_es_sich()
    {
        HttpClient client = await SignedInAsync();

        HttpResponseMessage saved = await client.PutAsJsonAsync("/api/organization", new
        {
            name = "Theater am Kanal e. V.",
            legalForm = "eingetragener Verein",
            chartOfAccounts = 42,
            isNonProfit = true,
            city = "Berlin",
        });
        saved.StatusCode.Should().Be(HttpStatusCode.OK);

        OrganizationDto? organization = await client.GetFromJsonAsync<OrganizationDto>("/api/organization");

        organization.Should().NotBeNull();
        organization.ChartOfAccounts.Should().Be(42);
        organization.IsNonProfit.Should().BeTrue();
        organization.RequiresTaxSpheres.Should().BeTrue();
        organization.ChartLabel.Should().Contain("SKR42");
    }

    [Fact]
    public async Task Ein_unbekannter_Kontenrahmen_wird_abgewiesen()
    {
        HttpClient client = await SignedInAsync();

        HttpResponseMessage response = await client.PutAsJsonAsync("/api/organization", new
        {
            name = "Irgendwas",
            chartOfAccounts = 77,
            isNonProfit = false,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Ohne_Namen_bleibt_das_Haus_wie_es_war()
    {
        HttpClient client = await SignedInAsync();

        HttpResponseMessage response = await client.PutAsJsonAsync("/api/organization", new
        {
            name = "   ",
            chartOfAccounts = 3,
            isNonProfit = false,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("braucht einen Namen");
    }

    private record ChartDto(int Chart, string Key, string Label, bool UsesTaxSpheres);

    private record SphereDto(int Sphere, string Key, string Label, string Example);

    private record OrganizationDto(
        string Name, int ChartOfAccounts, string ChartLabel, bool IsNonProfit, bool RequiresTaxSpheres);
}
