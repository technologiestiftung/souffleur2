using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;

namespace Souffleur.Api.Tests;

/// <summary>
/// Der Kalender zum Abonnieren. Die Adresse wird von fremder Software
/// abgefragt, ohne Anmeldung -- deshalb steht hier, was sie beantwortet und
/// was nicht.
/// </summary>
public class SubscriptionApiTests(SouffleurApiFactory factory) : IClassFixture<SouffleurApiFactory>
{
    private async Task<HttpClient> SignedInAsync()
    {
        HttpClient client = factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
            });

        await client.PostAsJsonAsync("/api/account/login", new
        {
            email = SouffleurApiFactory.AdminEmail,
            password = SouffleurApiFactory.AdminPassword,
            rememberMe = false,
        });

        return client;
    }

    private static async Task<SubscriptionDto> SubscribeAsync(HttpClient client, string name)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/calendar/subscriptions", new { name, includeCancelled = true });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<SubscriptionDto>())!;
    }

    private static async Task EveningAsync(HttpClient client, string title, int day) =>
        (await client.PostAsJsonAsync("/api/calendar/events", new
        {
            title,
            startsAt = new DateTimeOffset(2027, 5, day, 19, 0, 0, TimeSpan.Zero),
            endsAt = new DateTimeOffset(2027, 5, day, 21, 0, 0, TimeSpan.Zero),
        })).StatusCode.Should().Be(HttpStatusCode.Created);

    [Fact]
    public async Task Die_Adresse_liefert_einen_Kalender_ohne_Anmeldung()
    {
        HttpClient client = await SignedInAsync();
        await EveningAsync(client, "Premiere im Feed", 3);
        SubscriptionDto subscription = await SubscribeAsync(client, "Spielplan");

        // Ein frischer Zugang ohne Sitzung -- so fragt eine Kalenderanwendung.
        HttpClient anonymous = factory.CreateClient();
        HttpResponseMessage feed = await anonymous.GetAsync(
            $"/api/calendar/feed/{subscription.Token}.ics");

        feed.StatusCode.Should().Be(HttpStatusCode.OK);
        feed.Content.Headers.ContentType!.MediaType.Should().Be("text/calendar");

        string body = await feed.Content.ReadAsStringAsync();
        body.Should().StartWith("BEGIN:VCALENDAR");
        body.Should().Contain("SUMMARY:Premiere im Feed");
        body.Should().Contain("X-WR-CALNAME:Spielplan");
    }

    [Fact]
    public async Task Die_Adresse_steht_in_der_Antwort_und_ist_vollstaendig()
    {
        HttpClient client = await SignedInAsync();
        SubscriptionDto subscription = await SubscribeAsync(client, "Mit Adresse");

        subscription.Address.Should().EndWith($"/api/calendar/feed/{subscription.Token}.ics");
        subscription.Address.Should().StartWith("http");
    }

    [Fact]
    public async Task Eine_unbekannte_Adresse_antwortet_mit_404()
    {
        HttpResponseMessage feed = await factory.CreateClient()
            .GetAsync("/api/calendar/feed/gibtesnicht.ics");

        feed.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Ein_zurueckgezogenes_Abonnement_antwortet_nicht_mehr()
    {
        HttpClient client = await SignedInAsync();
        SubscriptionDto subscription = await SubscribeAsync(client, "Zum Zurückziehen");
        HttpClient anonymous = factory.CreateClient();

        (await anonymous.GetAsync($"/api/calendar/feed/{subscription.Token}.ics"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        HttpResponseMessage rotated = await client.PostAsJsonAsync(
            $"/api/calendar/subscriptions/{subscription.Id}/rotate", new { });
        rotated.StatusCode.Should().Be(HttpStatusCode.OK);

        SubscriptionDto renewed = (await rotated.Content.ReadFromJsonAsync<SubscriptionDto>())!;
        renewed.Token.Should().NotBe(subscription.Token);

        // Die alte Adresse ist tot, die neue antwortet.
        (await anonymous.GetAsync($"/api/calendar/feed/{subscription.Token}.ics"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await anonymous.GetAsync($"/api/calendar/feed/{renewed.Token}.ics"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unveraendert_antwortet_der_Feed_mit_304()
    {
        HttpClient client = await SignedInAsync();
        SubscriptionDto subscription = await SubscribeAsync(client, "Mit Etikett");
        HttpClient anonymous = factory.CreateClient();

        HttpResponseMessage first = await anonymous.GetAsync(
            $"/api/calendar/feed/{subscription.Token}.ics");
        EntityTagHeaderValue tag = first.Headers.ETag!;

        var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/calendar/feed/{subscription.Token}.ics");
        request.Headers.IfNoneMatch.Add(tag);

        // Spart der Kalenderanwendung den Abgleich -- und dem Haus die Last,
        // wenn zwanzig Telefone stündlich fragen.
        (await anonymous.SendAsync(request)).StatusCode
            .Should().Be(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task Ein_abgeschaltetes_Abonnement_antwortet_nicht_mehr()
    {
        HttpClient client = await SignedInAsync();
        SubscriptionDto subscription = await SubscribeAsync(client, "Zum Abschalten");

        (await client.PutAsJsonAsync(
            $"/api/calendar/subscriptions/{subscription.Id}",
            new { name = "Zum Abschalten", isActive = false })).StatusCode
            .Should().Be(HttpStatusCode.OK);

        (await factory.CreateClient().GetAsync($"/api/calendar/feed/{subscription.Token}.ics"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Ein_Abonnement_mit_Filter_laesst_das_Uebrige_weg()
    {
        HttpClient client = await SignedInAsync();

        List<CategoryDto>? categories = await client.GetFromJsonAsync<List<CategoryDto>>(
            "/api/catalogs/EventCategory");
        Guid premiere = categories!.First(c => c.Key == "premiere").Id;

        (await client.PostAsJsonAsync("/api/calendar/events", new
        {
            title = "Nur die Premiere",
            categoryId = premiere,
            startsAt = new DateTimeOffset(2027, 6, 4, 19, 0, 0, TimeSpan.Zero),
            endsAt = new DateTimeOffset(2027, 6, 4, 21, 0, 0, TimeSpan.Zero),
        })).StatusCode.Should().Be(HttpStatusCode.Created);

        await EveningAsync(client, "Ohne Kategorie", 5);

        HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/calendar/subscriptions",
            new { name = "Nur Premieren", categoryIds = new[] { premiere } });

        SubscriptionDto subscription = (await created.Content.ReadFromJsonAsync<SubscriptionDto>())!;

        string body = await factory.CreateClient()
            .GetStringAsync($"/api/calendar/feed/{subscription.Token}.ics");

        body.Should().Contain("SUMMARY:Nur die Premiere");
        body.Should().NotContain("SUMMARY:Ohne Kategorie");
    }

    [Fact]
    public async Task Ein_Abonnement_auf_die_eigene_Person_zeigt_nur_die_eigenen_Dienste()
    {
        HttpClient client = await SignedInAsync();

        HttpResponseMessage created = await client.PostAsJsonAsync("/api/contacts", new
        {
            kind = 2,
            givenName = "Clara",
            surname = "Weber",
            roles = new[] { 5 },
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        Guid clara = (await created.Content.ReadFromJsonAsync<ContactDto>())!.Id;

        (await client.PostAsJsonAsync("/api/calendar/events", new
        {
            title = "Mit Clara",
            startsAt = new DateTimeOffset(2027, 7, 6, 19, 0, 0, TimeSpan.Zero),
            endsAt = new DateTimeOffset(2027, 7, 6, 21, 0, 0, TimeSpan.Zero),
            participants = new[] { new { contactId = clara } },
        })).StatusCode.Should().Be(HttpStatusCode.Created);

        await EveningAsync(client, "Ohne Clara", 7);

        HttpResponseMessage answer = await client.PostAsJsonAsync(
            "/api/calendar/subscriptions",
            new { name = "Dienstplan Clara", contactIds = new[] { clara } });

        SubscriptionDto subscription = (await answer.Content.ReadFromJsonAsync<SubscriptionDto>())!;

        string body = await factory.CreateClient()
            .GetStringAsync($"/api/calendar/feed/{subscription.Token}.ics");

        body.Should().Contain("SUMMARY:Mit Clara");
        body.Should().NotContain("SUMMARY:Ohne Clara");
    }

    [Fact]
    public async Task Ohne_Anmeldung_laesst_sich_kein_Abonnement_anlegen()
    {
        HttpResponseMessage response = await factory.CreateClient()
            .PostAsJsonAsync("/api/calendar/subscriptions", new { name = "Heimlich" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private record SubscriptionDto(
        Guid Id, string Name, string Token, string Address, bool IsActive, int FetchCount);

    private record CategoryDto(Guid Id, string Key, string Label);

    private record ContactDto(Guid Id, string Name);
}
