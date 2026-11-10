using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Souffleur.Api.Tests;

/// <summary>
/// Der Kontenplan in den Einstellungen: anlegen, aendern, stilllegen,
/// einlesen -- und was dabei abgewiesen wird.
/// </summary>
public class KontenplanApiTests(SouffleurApiFactory factory) : IClassFixture<SouffleurApiFactory>
{
    private async Task<HttpClient> SignedInAsync()
    {
        HttpClient client = factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = true });

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
    public async Task Ein_Konto_laesst_sich_aendern_und_stilllegen_die_Nummer_bleibt()
    {
        HttpClient client = await SignedInAsync();
        HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/accounting/accounts", new { number = "4911", name = "Porto", type = 5 });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        AccountDto account = (await created.Content.ReadFromJsonAsync<AccountDto>())!;

        HttpResponseMessage changed = await client.PutAsJsonAsync(
            $"/api/accounting/accounts/{account.Id}",
            new { name = "Porto und Versand", type = 5, taxKey = "", isActive = false });
        changed.StatusCode.Should().Be(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());

        List<AccountDto>? active = await client.GetFromJsonAsync<List<AccountDto>>("/api/accounting/accounts");
        active.Should().NotContain(a => a.Id == account.Id, "stillgelegt heisst: nicht mehr zum Buchen angeboten");

        List<AccountDto>? all = await client.GetFromJsonAsync<List<AccountDto>>(
            "/api/accounting/accounts?includeInactive=true");
        all.Should().ContainSingle(a => a.Id == account.Id)
           .Which.Should().Be(account with { Name = "Porto und Versand", IsActive = false });
    }

    [Fact]
    public async Task Ein_Konto_ohne_Namen_oder_mit_Buchstaben_in_der_Nummer_wird_abgewiesen()
    {
        HttpClient client = await SignedInAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/accounting/accounts", new { number = "48A0", name = " ", type = 5 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("nur aus Ziffern").And.Contain("Name ist nötig");
    }

    [Fact]
    public async Task Das_Einlesen_nennt_unbrauchbare_Zeilen_statt_sie_anzulegen()
    {
        HttpClient client = await SignedInAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/accounting/accounts/import", new
        {
            accounts = new object[]
            {
                new { number = "8401", name = "Erlöse Eintritt Abend", type = 4 },
                new { number = "8402", name = "", type = 4 },
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ImportDto? result = await response.Content.ReadFromJsonAsync<ImportDto>();
        result!.Added.Should().Be(1);
        result.Rejected.Should().Equal("8402");
    }

    private record AccountDto(Guid Id, string Number, string Name, int Type, string? TaxKey, int? DefaultTaxSphere, bool IsActive);

    private record ImportDto(int Added, List<string> SkippedBecauseTheyExist, List<string> Rejected);
}
