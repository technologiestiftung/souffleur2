using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Souffleur.Api.Tests;

/// <summary>
/// Konten anlegen und einladen: Die Verwaltung vergibt kein Passwort, die
/// Person setzt es selbst ueber einen Link, der einmal gilt.
/// </summary>
public class EinladungApiTests(SouffleurApiFactory factory) : IClassFixture<SouffleurApiFactory>
{
    private HttpClient Client() => factory.CreateClient(
        new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = true });

    private async Task<HttpClient> AdminAsync()
    {
        HttpClient client = Client();
        (await client.PostAsJsonAsync("/api/account/login", new
        {
            email = SouffleurApiFactory.AdminEmail,
            password = SouffleurApiFactory.AdminPassword,
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        return client;
    }

    private static async Task<InvitationDto> EinladenAsync(HttpClient admin, string email)
    {
        HttpResponseMessage response = await admin.PostAsJsonAsync("/api/access/users", new
        {
            givenName = "Ida", surname = "Einladung", email, roleIds = Array.Empty<Guid>(),
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<InvitationDto>())!;
    }

    private static string TokenAus(string link) =>
        Uri.UnescapeDataString(link[(link.IndexOf("token=", StringComparison.Ordinal) + 6)..]);

    private static string Adresse() => $"ida-{Guid.NewGuid():N}@example.invalid";

    [Fact]
    public async Task Die_Eingeladene_setzt_ihr_Passwort_selbst_und_der_Link_gilt_nur_einmal()
    {
        HttpClient admin = await AdminAsync();
        string email = Adresse();
        InvitationDto invitation = await EinladenAsync(admin, email);
        invitation.Link.Should().Contain("/einladung?user=");
        string token = TokenAus(invitation.Link);

        HttpClient fremd = Client();
        InfoDto? info = await fremd.GetFromJsonAsync<InfoDto>(
            $"/api/account/invitation?user={invitation.UserId}&token={Uri.EscapeDataString(token)}");
        info!.Name.Should().Be("Ida Einladung");

        // Zu kurz -- und der Grund kommt auf Deutsch.
        HttpResponseMessage kurz = await fremd.PostAsJsonAsync("/api/account/invitation",
            new { user = invitation.UserId, token, password = "kurz" });
        kurz.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await kurz.Content.ReadAsStringAsync()).Should().Contain("mindestens 12 Zeichen");

        (await fremd.PostAsJsonAsync("/api/account/invitation",
            new { user = invitation.UserId, token, password = "Probebühne2027abc" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await fremd.PostAsJsonAsync("/api/account/login", new { email, password = "Probebühne2027abc" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Ein zweites Mal geht derselbe Link nicht.
        (await Client().PostAsJsonAsync("/api/account/invitation",
            new { user = invitation.UserId, token, password = "NochEinmal2027abc" }))
            .StatusCode.Should().Be(HttpStatusCode.Gone);
    }

    [Fact]
    public async Task Ein_neuer_Link_macht_den_alten_ungueltig()
    {
        HttpClient admin = await AdminAsync();
        InvitationDto erste = await EinladenAsync(admin, Adresse());

        HttpResponseMessage again = await admin.PostAsync($"/api/access/users/{erste.UserId}/invitation", null);
        again.StatusCode.Should().Be(HttpStatusCode.OK);

        HttpResponseMessage alt = await Client().GetAsync(
            $"/api/account/invitation?user={erste.UserId}&token={Uri.EscapeDataString(TokenAus(erste.Link))}");
        alt.StatusCode.Should().Be(HttpStatusCode.Gone);
    }

    [Fact]
    public async Task Ein_gesperrtes_Konto_kommt_nicht_mehr_herein()
    {
        HttpClient admin = await AdminAsync();
        string email = Adresse();
        InvitationDto invitation = await EinladenAsync(admin, email);
        await Client().PostAsJsonAsync("/api/account/invitation",
            new { user = invitation.UserId, token = TokenAus(invitation.Link), password = "Probebühne2027abc" });

        (await admin.PutAsJsonAsync($"/api/access/users/{invitation.UserId}/active", new { isActive = false }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Client().PostAsJsonAsync("/api/account/login", new { email, password = "Probebühne2027abc" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Eine_Adresse_gibt_es_nur_einmal()
    {
        HttpClient admin = await AdminAsync();

        HttpResponseMessage response = await admin.PostAsJsonAsync("/api/access/users", new
        {
            givenName = "Doppelt", surname = "Admin", email = SouffleurApiFactory.AdminEmail,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("gibt es schon ein Konto");
    }

    [Fact]
    public async Task Ohne_Verwaltungsrecht_legt_niemand_Konten_an()
    {
        HttpResponseMessage response = await Client().PostAsJsonAsync("/api/access/users", new
        {
            givenName = "Fremd", surname = "Person", email = Adresse(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private record InvitationDto(Guid UserId, string Link, bool Mailed, DateTimeOffset ValidUntil);

    private record InfoDto(string Name, string Email);
}
