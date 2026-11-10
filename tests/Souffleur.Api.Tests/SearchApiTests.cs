using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Souffleur.Domain.Accounting;
using Souffleur.Domain.Documents;
using Souffleur.Domain.Identity;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Api.Tests;

/// <summary>
/// Das eine Suchfeld. Geprueft wird weniger, OB etwas gefunden wird, als WER
/// was findet: Die Suche darf nicht zeigen, was die Listen verbergen.
/// </summary>
public class SearchApiTests(SouffleurApiFactory factory) : IClassFixture<SouffleurApiFactory>
{
    private const string Passwort = "SuchTestPasswort123";

    private sealed record Treffer(Guid Id, string Titel, string? Untertitel, string Link);

    private sealed record Bereich(string Key, string Label, string Icon, List<Treffer> Treffer);

    private HttpClient CreateClient() =>
        factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
        });

    private static async Task AnmeldenAsync(HttpClient client, string email, string passwort)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/account/login",
            new { email, password = passwort, rememberMe = false });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<HttpClient> AdminAsync()
    {
        HttpClient client = CreateClient();
        await AnmeldenAsync(client, SouffleurApiFactory.AdminEmail, SouffleurApiFactory.AdminPassword);
        return client;
    }

    /// <summary>Ein Konto mit genau den Rechten, die der Test braucht -- ueber eine Gruppe, wie im Haus.</summary>
    private async Task<HttpClient> KontoAsync(string kennung, params Permission[] rechte)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var context = scope.ServiceProvider.GetRequiredService<SouffleurDbContext>();

        string email = $"{kennung}@test.local";
        var user = new User
        {
            UserName = email, Email = email, EmailConfirmed = true, GivenName = "Such", Surname = kennung,
        };
        IdentityResult result = await users.CreateAsync(user, Passwort);
        result.Succeeded.Should().BeTrue(because: string.Join(", ", result.Errors.Select(e => e.Description)));

        if (rechte.Length > 0)
        {
            var rolle = new Role { Id = Guid.CreateVersion7(), Name = $"Rolle-{kennung}" };
            var gruppe = new Group { Name = $"Gruppe-{kennung}" };
            context.Roles.Add(rolle);
            context.RolePermissions.AddRange(rechte.Select(r => new RolePermission { RoleId = rolle.Id, Permission = r }));
            context.Groups.Add(gruppe);
            context.GroupRoles.Add(new GroupRole { GroupId = gruppe.Id, RoleId = rolle.Id });
            context.GroupMembers.Add(new GroupMember { GroupId = gruppe.Id, UserId = user.Id });
            await context.SaveChangesAsync();
        }

        HttpClient client = CreateClient();
        await AnmeldenAsync(client, email, Passwort);
        return client;
    }

    private static async Task<List<Bereich>> SucheAsync(HttpClient client, string q)
    {
        HttpResponseMessage response = await client.GetAsync($"/api/search?q={Uri.EscapeDataString(q)}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<Bereich>>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
    }

    [Fact]
    public async Task Ohne_Anmeldung_gibt_es_nichts()
    {
        HttpResponseMessage response = await CreateClient().GetAsync("/api/search?q=hamlet");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Ein_Zeichen_ist_noch_keine_Suche()
    {
        HttpClient admin = await AdminAsync();
        (await SucheAsync(admin, "a")).Should().BeEmpty();
        (await SucheAsync(admin, "   ")).Should().BeEmpty();
    }

    [Fact]
    public async Task Findet_einen_Kontakt_in_beliebiger_Wortfolge_und_fuehrt_hin()
    {
        HttpClient admin = await AdminAsync();
        HttpResponseMessage angelegt = await admin.PostAsJsonAsync("/api/contacts", new
        {
            kind = 1,
            roles = new[] { 1 },
            name = "Zimmermann, Suchtestine",
            city = "Pankow",
        });
        angelegt.StatusCode.Should().Be(HttpStatusCode.Created);
        Guid id = (await angelegt.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Beide Woerter muessen vorkommen, die Reihenfolge ist egal.
        List<Bereich> funde = await SucheAsync(admin, "pankow suchtestine");

        Bereich kontakte = funde.Should().ContainSingle(b => b.Key == "kontakte").Subject;
        Treffer treffer = kontakte.Treffer.Should().ContainSingle(t => t.Id == id).Subject;
        treffer.Titel.Should().Be("Zimmermann, Suchtestine");
        treffer.Link.Should().Be($"/kontakte?kontakt={id}");

        // Ein Wort, das nirgends steht, laesst den Fund wieder verschwinden.
        (await SucheAsync(admin, "suchtestine xyzzy")).Should().NotContain(b => b.Key == "kontakte");
    }

    [Fact]
    public async Task Platzhalter_aus_der_Eingabe_zaehlen_nicht()
    {
        HttpClient admin = await AdminAsync();
        // Ein Prozentzeichen allein faende sonst jeden Kontakt.
        (await SucheAsync(admin, "%%")).Should().NotContain(b => b.Key == "kontakte");
    }

    [Fact]
    public async Task Dokumente_sieht_nur_wer_sie_lesen_darf_und_eingeschraenkte_nur_die_Gemeinten()
    {
        Guid offen, eingeschraenkt;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SouffleurDbContext>();
            Document Dokument(string name, bool restricted) => new()
            {
                Name = name,
                OriginalName = name + ".pdf",
                ContentType = "application/pdf",
                SizeInBytes = 10,
                StoredAs = Guid.NewGuid().ToString("N"),
                Sha256 = Guid.NewGuid().ToString("N"),
                IsRestricted = restricted,
            };
            Document a = Dokument("Gastspielvertrag Orchestergraben", false);
            Document b = Dokument("Personalakte Orchestergraben", true);
            context.Documents.AddRange(a, b);
            await context.SaveChangesAsync();
            (offen, eingeschraenkt) = (a.Id, b.Id);
        }

        // Die Verwaltung sieht beides.
        HttpClient admin = await AdminAsync();
        Bereich alles = (await SucheAsync(admin, "orchestergraben")).Should().ContainSingle(b => b.Key == "dokumente").Subject;
        alles.Treffer.Select(t => t.Id).Should().Contain([offen, eingeschraenkt]);
        alles.Treffer.Should().OnlyContain(t => t.Link == $"/dokumente?dokument={t.Id}");

        // Wer lesen darf, sieht nur das Offene.
        HttpClient leser = await KontoAsync("dokleser", Permission.DocumentsRead);
        Bereich offene = (await SucheAsync(leser, "orchestergraben")).Should().ContainSingle(b => b.Key == "dokumente").Subject;
        offene.Treffer.Select(t => t.Id).Should().Contain(offen).And.NotContain(eingeschraenkt);

        // Ohne das Recht gibt es den Bereich nicht.
        HttpClient niemand = await KontoAsync("dokniemand");
        (await SucheAsync(niemand, "orchestergraben")).Should().NotContain(b => b.Key == "dokumente");
    }

    [Fact]
    public async Task Finanzen_sieht_nur_wer_Buchungen_lesen_darf()
    {
        Guid id;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SouffleurDbContext>();
            var buchung = new JournalEntry
            {
                FiscalYear = 2026,
                Date = new DateOnly(2026, 3, 14),
                Description = "Scheinwerfermiete Premierenwoche",
            };
            context.JournalEntries.Add(buchung);
            await context.SaveChangesAsync();
            id = buchung.Id;
        }

        HttpClient admin = await AdminAsync();
        Bereich finanzen = (await SucheAsync(admin, "scheinwerfermiete")).Should().ContainSingle(b => b.Key == "finanzen").Subject;
        finanzen.Treffer.Should().ContainSingle(t => t.Id == id).Which.Link.Should().Be($"/finanzen?buchung={id}");

        HttpClient kollege = await KontoAsync("finniemand", Permission.DocumentsRead);
        (await SucheAsync(kollege, "scheinwerfermiete")).Should().NotContain(b => b.Key == "finanzen");
    }

    [Fact]
    public async Task Konten_und_Gruppen_findet_nur_die_Verwaltung()
    {
        HttpClient admin = await AdminAsync();
        List<Bereich> funde = await SucheAsync(admin, "admin@test.local");
        funde.Should().ContainSingle(b => b.Key == "haus")
            .Which.Treffer.Should().Contain(t => t.Link == "/einstellungen/zugaenge");

        HttpClient kollege = await KontoAsync("hausniemand");
        (await SucheAsync(kollege, "admin@test.local")).Should().NotContain(b => b.Key == "haus");
    }
}
