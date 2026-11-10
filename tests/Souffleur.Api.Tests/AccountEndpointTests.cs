using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Souffleur.Api.Tests;

public class AccountEndpointTests(SouffleurApiFactory factory) : IClassFixture<SouffleurApiFactory>
{
    private HttpClient CreateClient() =>
        factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
        });

    [Fact]
    public async Task Health_antwortet_ohne_Anmeldung()
    {
        HttpResponseMessage response = await CreateClient().GetAsync("/api/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Me_ohne_Anmeldung_ist_401()
    {
        HttpResponseMessage response = await CreateClient().GetAsync("/api/account/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Anmeldung_mit_falschem_Passwort_ist_401()
    {
        HttpResponseMessage response = await CreateClient().PostAsJsonAsync(
            "/api/account/login",
            new { email = SouffleurApiFactory.AdminEmail, password = "falsch", rememberMe = false });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Unbekannte_Adresse_antwortet_wie_falsches_Passwort()
    {
        HttpResponseMessage response = await CreateClient().PostAsJsonAsync(
            "/api/account/login",
            new { email = "gibtesnicht@test.local", password = "egal", rememberMe = false });

        // Gleiche Antwort wie oben -- die API verraet nicht, welche Adressen ein Konto haben.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Angemeldet_liefert_me_das_Konto_mit_Administratorrolle()
    {
        HttpClient client = CreateClient();

        HttpResponseMessage login = await client.PostAsJsonAsync(
            "/api/account/login",
            new
            {
                email = SouffleurApiFactory.AdminEmail,
                password = SouffleurApiFactory.AdminPassword,
                rememberMe = false,
            });
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        CurrentUser? user = await client.GetFromJsonAsync<CurrentUser>("/api/account/me");
        user.Should().NotBeNull();
        user.Email.Should().Be(SouffleurApiFactory.AdminEmail);
        user.Roles.Should().Contain("Administrator");
    }

    [Fact]
    public async Task Nach_dem_Abmelden_ist_me_wieder_401()
    {
        HttpClient client = CreateClient();
        await client.PostAsJsonAsync(
            "/api/account/login",
            new
            {
                email = SouffleurApiFactory.AdminEmail,
                password = SouffleurApiFactory.AdminPassword,
                rememberMe = false,
            });

        HttpResponseMessage logout = await client.PostAsync("/api/account/logout", null);
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage me = await client.GetAsync("/api/account/me");
        me.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private record CurrentUser(Guid Id, string Email, string DisplayName, string[] Roles);
}
