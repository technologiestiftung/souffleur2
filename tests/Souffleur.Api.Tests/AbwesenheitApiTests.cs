using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Souffleur.Domain.Contacts;
using Souffleur.Domain.Identity;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Api.Tests;

/// <summary>
/// Wer an Abwesenheiten was darf -- durch die ganze Anwendung. Krankmeldungen
/// sind Gesundheitsdaten, und die Grenze muss an der Schnittstelle halten,
/// nicht nur in der Oberflaeche.
/// </summary>
public class AbwesenheitApiTests(SouffleurApiFactory factory) : IClassFixture<SouffleurApiFactory>
{
    private const string Passwort = "MitarbeitTest123";

    private HttpClient CreateClient() =>
        factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
        });

    /// <summary>Ein Konto mit Kontakt, wahlweise mit einem Recht ueber eine Gruppe.</summary>
    private async Task<(string Email, Guid ContactId)> PersonAsync(string kennung, Permission? recht = null)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var context = scope.ServiceProvider.GetRequiredService<SouffleurDbContext>();

        string email = $"{kennung}@test.local";
        var user = new User
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            GivenName = "Test",
            Surname = kennung,
        };

        (await users.CreateAsync(user, Passwort)).Succeeded.Should().BeTrue();

        var contact = new Contact
        {
            Kind = ContactKind.Person,
            Name = $"{kennung}, Test",
            GivenName = "Test",
            Surname = kennung,
            UserId = user.Id,
        };
        context.Contacts.Add(contact);

        if (recht is { } r)
        {
            var rolle = new Role { Id = Guid.CreateVersion7(), Name = $"Rolle-{kennung}" };
            var gruppe = new Group { Name = $"Gruppe-{kennung}" };

            context.Roles.Add(rolle);
            context.RolePermissions.Add(new RolePermission { RoleId = rolle.Id, Permission = r });
            context.Groups.Add(gruppe);
            context.GroupRoles.Add(new GroupRole { GroupId = gruppe.Id, RoleId = rolle.Id });
            context.GroupMembers.Add(new GroupMember { GroupId = gruppe.Id, UserId = user.Id });
        }

        await context.SaveChangesAsync();
        return (email, contact.Id);
    }

    private async Task<HttpClient> AngemeldetAsync(string email)
    {
        HttpClient client = CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/account/login",
            new { email, password = Passwort, rememberMe = false });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return client;
    }

    private static object Antrag(int art, string von, string bis, Guid? fuer = null) => new
    {
        contactId = fuer,
        kind = art,
        startDate = von,
        endDate = bis,
        isHalfDay = false,
        note = (string?)null,
        certificateReceived = false,
    };

    [Fact]
    public async Task Mitarbeit_beantragt_selbst_aber_sieht_keine_fremden()
    {
        (string anna, _) = await PersonAsync("mitarbeit-anna");
        (_, Guid ben) = await PersonAsync("mitarbeit-ben");
        HttpClient client = await AngemeldetAsync(anna);

        (await client.PostAsJsonAsync("/api/personnel/absences", Antrag(1, "2026-08-03", "2026-08-07")))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.GetAsync("/api/personnel/absences?from=2026-01-01&to=2026-12-31&all=true"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await client.GetAsync($"/api/personnel/absences?from=2026-01-01&to=2026-12-31&contactId={ben}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await client.PostAsJsonAsync("/api/personnel/absences", Antrag(7, "2026-08-10", "2026-08-10", ben)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await client.GetAsync("/api/personnel/absences/open"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Wer_entscheidet_sieht_den_Antrag_und_genehmigt_ihn()
    {
        (string clara, _) = await PersonAsync("antrag-clara");
        (string leitung, _) = await PersonAsync("antrag-leitung", Permission.PersonnelApprove);

        HttpClient selbst = await AngemeldetAsync(clara);
        HttpResponseMessage gestellt = await selbst.PostAsJsonAsync(
            "/api/personnel/absences", Antrag(1, "2026-09-07", "2026-09-11"));
        gestellt.StatusCode.Should().Be(HttpStatusCode.OK);
        Guid id = (await gestellt.Content.ReadFromJsonAsync<AntragAntwort>())!.Id;

        // Selbst entscheiden geht nicht -- dafuer gibt es die Leitung.
        (await selbst.PostAsJsonAsync($"/api/personnel/absences/{id}/decide", new { approve = true }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        HttpClient chef = await AngemeldetAsync(leitung);
        List<AntragAntwort>? offen = await chef.GetFromJsonAsync<List<AntragAntwort>>("/api/personnel/absences/open");
        offen.Should().Contain(a => a.Id == id);

        HttpResponseMessage entschieden = await chef.PostAsJsonAsync(
            $"/api/personnel/absences/{id}/decide", new { approve = true });
        entschieden.StatusCode.Should().Be(HttpStatusCode.OK);
        (await entschieden.Content.ReadFromJsonAsync<AntragAntwort>())!.StatusLabel.Should().Be("Genehmigt");
    }

    [Fact]
    public async Task Vertragsdaten_pflegt_nur_wer_das_Recht_hat()
    {
        (string david, Guid davidId) = await PersonAsync("vertrag-david");
        HttpClient client = await AngemeldetAsync(david);

        var vertrag = new
        {
            contactId = davidId,
            validFrom = "2026-01-01",
            weeklyHours = 40,
            workDays = 31,
            annualLeaveDays = 30,
        };

        (await client.PostAsJsonAsync("/api/personnel/absences/employments", vertrag))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Den eigenen Stand sehen darf jede Person.
        (await client.GetAsync($"/api/personnel/absences/employments/{davidId}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (string personal, _) = await PersonAsync("vertrag-personal", Permission.PersonnelRecords);
        HttpClient verwaltung = await AngemeldetAsync(personal);

        (await verwaltung.PostAsJsonAsync("/api/personnel/absences/employments", vertrag))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Vertraege pflegen heisst nicht, Krankmeldungen zu sehen.
        (await verwaltung.GetAsync("/api/personnel/absences?from=2026-01-01&to=2026-12-31&all=true"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private sealed record AntragAntwort(Guid Id, string StatusLabel);
}
