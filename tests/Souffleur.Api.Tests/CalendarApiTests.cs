using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Souffleur.Api.Tests;

/// <summary>
/// Termine und Serien lesen, so wie der Bogen zum Bearbeiten sie braucht.
/// </summary>
public class CalendarApiTests(SouffleurApiFactory factory) : IClassFixture<SouffleurApiFactory>
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

    private static async Task<Guid> KontaktAsync(HttpClient client, string surname)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/contacts", new
        {
            kind = 2, givenName = "Mia", surname, roles = new[] { 5 },
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Der Bogen schickt beim Speichern die ganze Einteilung zurueck. Fehlte sie
    /// beim Lesen, loeschte jedes Speichern alle Eingeteilten.
    /// </summary>
    [Fact]
    public async Task Ein_Termin_kommt_mit_seiner_Einteilung_zurueck()
    {
        HttpClient client = await SignedInAsync();
        Guid person = await KontaktAsync(client, "Termineinteilung");

        HttpResponseMessage created = await client.PostAsJsonAsync("/api/calendar/events", new
        {
            title = "Probe mit Einteilung",
            startsAt = new DateTimeOffset(2027, 5, 3, 10, 0, 0, TimeSpan.Zero),
            endsAt = new DateTimeOffset(2027, 5, 3, 13, 0, 0, TimeSpan.Zero),
            participants = new[] { new { contactId = person } },
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        Guid id = (await created.Content.ReadFromJsonAsync<DetailDto>())!.Id;

        DetailDto? found = await client.GetFromJsonAsync<DetailDto>($"/api/calendar/events/{id}");

        found!.Participants.Should().ContainSingle().Which.ContactId.Should().Be(person);
    }

    [Fact]
    public async Task Eine_Serie_kommt_mit_ihrer_Einteilung_zurueck()
    {
        HttpClient client = await SignedInAsync();
        Guid person = await KontaktAsync(client, "Serieneinteilung");

        HttpResponseMessage created = await client.PostAsJsonAsync("/api/calendar/series", new
        {
            title = "Wochenprobe mit Einteilung",
            startDate = "2027-06-01",
            startTime = "10:00:00",
            durationMinutes = 120,
            frequency = 2,
            interval = 1,
            byWeekday = 2,
            count = 4,
            participants = new[] { new { contactId = person } },
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        Guid id = (await created.Content.ReadFromJsonAsync<DetailDto>())!.Id;

        DetailDto? found = await client.GetFromJsonAsync<DetailDto>($"/api/calendar/series/{id}");

        found!.Participants.Should().ContainSingle().Which.ContactId.Should().Be(person);
    }

    private record DetailDto(Guid Id, List<ParticipantDto> Participants);

    private record ParticipantDto(Guid ContactId);
}
