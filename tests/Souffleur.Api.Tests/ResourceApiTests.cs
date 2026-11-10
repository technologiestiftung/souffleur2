using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Souffleur.Api.Tests;

/// <summary>
/// Die Ressourcen über die Schnittstelle: anlegen, belegen, und was passiert,
/// wenn zwei Termine denselben Saal wollen.
/// </summary>
public class ResourceApiTests(SouffleurApiFactory factory) : IClassFixture<SouffleurApiFactory>
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

    private static async Task<Guid> StageAsync(HttpClient client, string name)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/resources", new { name, shortName = "GS", isExclusive = true });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        ResourceDto? created = await response.Content.ReadFromJsonAsync<ResourceDto>();
        return created!.Id;
    }

    private static object Evening(Guid resource, int day, int hour, bool force = false) => new
    {
        title = $"Termin {hour} Uhr",
        startsAt = new DateTimeOffset(2027, 3, day, hour, 0, 0, TimeSpan.Zero),
        endsAt = new DateTimeOffset(2027, 3, day, hour + 2, 0, 0, TimeSpan.Zero),
        resourceIds = new[] { resource },
        force,
    };

    [Fact]
    public async Task Die_Ressourcenliste_nennt_was_das_Haus_hat()
    {
        HttpClient client = await SignedInAsync();
        await StageAsync(client, "Saal für die Liste");

        List<ResourceDto>? found = await client.GetFromJsonAsync<List<ResourceDto>>("/api/resources");

        found.Should().Contain(r => r.Name == "Saal für die Liste" && r.IsExclusive);
    }

    [Fact]
    public async Task Eine_Doppelbelegung_wird_abgelehnt_und_benennt_den_Grund()
    {
        HttpClient client = await SignedInAsync();
        Guid stage = await StageAsync(client, "Saal für die Doppelbelegung");

        (await client.PostAsJsonAsync("/api/calendar/events", Evening(stage, 4, 19)))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        HttpResponseMessage second = await client.PostAsJsonAsync(
            "/api/calendar/events", Evening(stage, 4, 20));

        // 409 und nicht 400: Die Eingabe ist in Ordnung, die Welt steht im Weg.
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);

        ConflictsDto? conflicts = await second.Content.ReadFromJsonAsync<ConflictsDto>();
        conflicts!.Conflicts.Should().ContainSingle()
                  .Which.SubjectName.Should().Be("Saal für die Doppelbelegung");
    }

    [Fact]
    public async Task Mit_Nachdruck_geht_die_Doppelbelegung_doch()
    {
        HttpClient client = await SignedInAsync();
        Guid stage = await StageAsync(client, "Saal mit Nachdruck");

        await client.PostAsJsonAsync("/api/calendar/events", Evening(stage, 11, 19));

        HttpResponseMessage second = await client.PostAsJsonAsync(
            "/api/calendar/events", Evening(stage, 11, 20, force: true));

        second.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Vorab_laesst_sich_fragen_ob_der_Saal_frei_ist()
    {
        HttpClient client = await SignedInAsync();
        Guid stage = await StageAsync(client, "Saal für die Vorabfrage");

        await client.PostAsJsonAsync("/api/calendar/events", Evening(stage, 18, 19));

        HttpResponseMessage answer = await client.PostAsJsonAsync("/api/calendar/conflicts", new
        {
            resourceIds = new[] { stage },
            startsAt = new DateTimeOffset(2027, 3, 18, 20, 0, 0, TimeSpan.Zero),
            endsAt = new DateTimeOffset(2027, 3, 18, 22, 0, 0, TimeSpan.Zero),
        });

        answer.StatusCode.Should().Be(HttpStatusCode.OK);

        ConflictsDto? conflicts = await answer.Content.ReadFromJsonAsync<ConflictsDto>();
        conflicts!.Conflicts.Should().ContainSingle();
    }

    [Fact]
    public async Task Eine_belegte_Ressource_laesst_sich_nicht_loeschen()
    {
        HttpClient client = await SignedInAsync();
        Guid stage = await StageAsync(client, "Saal der bleibt");

        await client.PostAsJsonAsync("/api/calendar/events", Evening(stage, 25, 19));

        HttpResponseMessage deleted = await client.DeleteAsync($"/api/resources/{stage}");

        deleted.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Abschalten geht -- und nimmt sie aus der Auswahl.
        (await client.PostAsJsonAsync($"/api/resources/{stage}/active", new { isActive = false }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        List<ResourceDto>? active = await client.GetFromJsonAsync<List<ResourceDto>>("/api/resources");
        active.Should().NotContain(r => r.Id == stage);
    }

    [Fact]
    public async Task Ohne_Anmeldung_gibt_es_keine_Ressourcen()
    {
        HttpResponseMessage response = await factory.CreateClient().GetAsync("/api/resources");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private record ResourceDto(Guid Id, string Name, string Display, bool IsExclusive, int UsageCount);

    private record ConflictsDto(List<ConflictDto> Conflicts);

    private record ConflictDto(int Subject, Guid SubjectId, string SubjectName, string Title);
}
