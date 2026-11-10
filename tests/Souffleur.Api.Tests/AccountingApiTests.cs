using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Souffleur.Api.Tests;

/// <summary>
/// Der Weg über HTTP, so wie die Oberfläche ihn geht: anmelden, Konten anlegen,
/// buchen, festschreiben, stornieren.
/// </summary>
public class AccountingApiTests(SouffleurApiFactory factory) : IClassFixture<SouffleurApiFactory>
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
    public async Task Ohne_Anmeldung_kommt_niemand_ans_Journal()
    {
        HttpClient anonymous = factory.CreateClient();

        HttpResponseMessage response = await anonymous.GetAsync("/api/accounting/journal");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Der_ganze_Weg_von_der_Buchung_bis_zum_Storno()
    {
        HttpClient client = await SignedInAsync();
        // Acht Zeichen, nicht vier: Die Kontonummer muss eindeutig sein, und bei
        // vier Stellen kollidieren parallel laufende Tests gelegentlich.
        string suffix = Guid.NewGuid().ToString("N")[..8];

        // Zwei Konten anlegen, wie das Steuerbüro sie vorgibt.
        Guid rentId = await CreateAccountAsync(client, $"42{suffix}", "Miete", type: 5);
        Guid bankId = await CreateAccountAsync(client, $"12{suffix}", "Bank", type: 1);

        // Buchen: Miete vom Konto bezahlt.
        HttpResponseMessage created = await client.PostAsJsonAsync("/api/accounting/journal", new
        {
            fiscalYear = 2026,
            date = "2026-03-01",
            description = "Miete März",
            lines = new[]
            {
                new { accountId = rentId, debit = 1500.00m, credit = 0m },
                new { accountId = bankId, debit = 0m, credit = 1500.00m },
            },
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        JournalEntryDto? entry = await created.Content.ReadFromJsonAsync<JournalEntryDto>();
        entry.Should().NotBeNull();
        entry.Total.Should().Be(1500.00m);
        entry.IsCommitted.Should().BeFalse();

        // Festschreiben.
        HttpResponseMessage committed = await client.PostAsync(
            $"/api/accounting/journal/{entry.Id}/commit", null);
        committed.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Ein zweites Mal geht nicht.
        HttpResponseMessage again = await client.PostAsync(
            $"/api/accounting/journal/{entry.Id}/commit", null);
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Stornieren.
        HttpResponseMessage reversed = await client.PostAsJsonAsync(
            $"/api/accounting/journal/{entry.Id}/reverse",
            new { date = "2026-04-02", reason = "Rechnung doppelt erfasst" });
        reversed.StatusCode.Should().Be(HttpStatusCode.Created);

        JournalEntryDto? reversal = await reversed.Content.ReadFromJsonAsync<JournalEntryDto>();
        reversal!.ReversalOfId.Should().Be(entry.Id);
        reversal.Description.Should().Contain("doppelt erfasst");
    }

    [Fact]
    public async Task Eine_unausgeglichene_Buchung_wird_mit_Begruendung_abgelehnt()
    {
        HttpClient client = await SignedInAsync();
        // Acht Zeichen, nicht vier: Die Kontonummer muss eindeutig sein, und bei
        // vier Stellen kollidieren parallel laufende Tests gelegentlich.
        string suffix = Guid.NewGuid().ToString("N")[..8];
        Guid a = await CreateAccountAsync(client, $"43{suffix}", "Gagen", type: 5);
        Guid b = await CreateAccountAsync(client, $"13{suffix}", "Kasse", type: 1);

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/accounting/journal", new
        {
            fiscalYear = 2026,
            date = "2026-03-01",
            description = "Schief",
            lines = new[]
            {
                new { accountId = a, debit = 100m, credit = 0m },
                new { accountId = b, debit = 0m, credit = 90m },
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Soll und Haben");
    }

    private static async Task<Guid> CreateAccountAsync(
        HttpClient client, string number, string name, int type)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/accounting/accounts", new { number, name, type, taxKey = (string?)null });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        AccountDto? account = await response.Content.ReadFromJsonAsync<AccountDto>();
        return account!.Id;
    }

    private record AccountDto(Guid Id, string Number, string Name);

    private record JournalEntryDto(
        Guid Id, int Number, string Description, bool IsCommitted, Guid? ReversalOfId, decimal Total);
}
