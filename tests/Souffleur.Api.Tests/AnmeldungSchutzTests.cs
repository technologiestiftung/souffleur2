using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Souffleur.Api.Tests;

/// <summary>
/// Was die Anmeldung aushalten muss, sobald souffleur nicht mehr nur im
/// Hausnetz steht, sondern über einen Reverse Proxy aus dem Internet
/// erreichbar ist.
/// </summary>
public class AnmeldungSchutzTests(HttpsFactory factory) : IClassFixture<HttpsFactory>
{
    /// <summary>
    /// Jeder Test spricht unter eigener Adresse. Sonst erschöpft der Test
    /// des Zugriffszählers das Kontingent der anderen -- und nebenbei zeigt
    /// es, dass die Grenze je Absender gilt und nicht für alle zusammen.
    /// </summary>
    private HttpClient CreateClient(string absender)
    {
        HttpClient client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", absender);
        return client;
    }

    [Fact]
    public async Task Das_Sitzungscookie_gilt_nur_verschluesselt_und_nicht_fuer_Skripte()
    {
        HttpClient client = CreateClient("198.51.100.1");
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/account/login",
            new { email = SouffleurApiFactory.AdminEmail, password = SouffleurApiFactory.AdminPassword });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        string cookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("souffleur=", StringComparison.Ordinal));

        // Ohne secure schickt der Browser das Cookie auch über HTTP -- im
        // offenen WLAN mitlesbar. Hinter einem Reverse Proxy kommt die
        // Anfrage als HTTP an; nur die weitergereichten Header verraten,
        // dass der Browser HTTPS gesprochen hat.
        cookie.Should().Contain("secure", Exactly.Once(), because: "das Cookie gehört nicht über HTTP");
        cookie.Should().Contain("httponly", Exactly.Once(), because: "kein Skript muss es lesen");
        cookie.Should().Contain("samesite=strict", Exactly.Once(), because: "schützt vor fremden Formularen");
    }

    [Fact]
    public async Task Ein_weitergereichtes_Schema_wird_uebernommen()
    {
        HttpClient client = CreateClient("198.51.100.2");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Add("X-Forwarded-Proto", "https");

        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Zu_viele_Anmeldeversuche_werden_abgewiesen()
    {
        HttpClient client = CreateClient("198.51.100.3");
        var codes = new List<HttpStatusCode>();

        // Die Kontosperre greift je Konto. Hier geht es um den Absender:
        // jemanden, der Adressen durchprobiert oder den Endpunkt flutet.
        for (int versuch = 0; versuch < 8; versuch++)
        {
            HttpResponseMessage response = await client.PostAsJsonAsync(
                "/api/account/login",
                new { email = $"niemand{versuch}@example.invalid", password = "falsch" });
            codes.Add(response.StatusCode);
        }

        codes.Should().Contain(HttpStatusCode.TooManyRequests,
            "nach genug Versuchen muss der Absender abgewiesen werden, nicht nur das Konto gesperrt");
    }
}

/// <summary>
/// Eine Instanz, die sich verhält wie eine hinter einem Reverse Proxy:
/// Das Sitzungscookie gilt nur über verschlüsselte Verbindungen.
/// </summary>
public class HttpsFactory : SouffleurApiFactory
{
    protected override bool RequireHttps => true;

    /// <summary>
    /// Niedrig, damit der Test mit wenigen Versuchen auskommt. Geprüft wird
    /// damit der Mechanismus, nicht die Zahl: Die darf sich im Betrieb
    /// ändern, ohne dass hier etwas nachgezogen werden muss.
    /// </summary>
    protected override int? LoginAttemptsPerWindow => 5;
}
