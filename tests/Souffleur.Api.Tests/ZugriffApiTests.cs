using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Souffleur.Domain.Identity;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Api.Tests;

/// <summary>
/// Die Rechtepruefung an der Schnittstelle. Der Unterschied zu den Tests im
/// Infrastruktur-Projekt: Hier geht es durch die ganze Anwendung -- Anmelden,
/// Rechte in die Sitzung, Endpunkt.
/// </summary>
public class ZugriffApiTests(SouffleurApiFactory factory) : IClassFixture<SouffleurApiFactory>
{
    private const string Passwort = "MitarbeitTest123";

    private HttpClient CreateClient() =>
        factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
        });

    /// <summary>Legt ein Konto an -- wahlweise mit einer Gruppe, die eine Rolle traegt.</summary>
    private async Task<string> KontoAsync(string kennung, Permission? ueberGruppe = null)
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

        IdentityResult result = await users.CreateAsync(user, Passwort);
        result.Succeeded.Should().BeTrue(because: string.Join(", ", result.Errors.Select(e => e.Description)));

        if (ueberGruppe is { } recht)
        {
            var rolle = new Role { Id = Guid.CreateVersion7(), Name = $"Rolle-{kennung}" };
            var gruppe = new Group { Name = $"Gruppe-{kennung}" };

            context.Roles.Add(rolle);
            context.RolePermissions.Add(new RolePermission { RoleId = rolle.Id, Permission = recht });
            context.Groups.Add(gruppe);
            context.GroupRoles.Add(new GroupRole { GroupId = gruppe.Id, RoleId = rolle.Id });
            context.GroupMembers.Add(new GroupMember { GroupId = gruppe.Id, UserId = user.Id });
            await context.SaveChangesAsync();
        }

        return email;
    }

    private static async Task AnmeldenAsync(HttpClient client, string email, string passwort)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/account/login",
            new { email, password = passwort, rememberMe = false });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ohne_Recht_wird_abgewiesen()
    {
        string email = await KontoAsync("ohnerecht");
        HttpClient client = CreateClient();
        await AnmeldenAsync(client, email, Passwort);

        // Angemeldet ja, berechtigt nein: Die Module schaltet nur, wer darf.
        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/modules/{(int)Souffleur.Domain.Modules.SouffleurModule.Ticketing}",
            new { isEnabled = true });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Eine_Gruppe_reicht_aus_um_zu_duerfen()
    {
        string email = await KontoAsync("ingruppe", Permission.ModulesManage);
        HttpClient client = CreateClient();
        await AnmeldenAsync(client, email, Passwort);

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/modules/{(int)Souffleur.Domain.Modules.SouffleurModule.Ticketing}",
            new { isEnabled = true });

        // Das Recht kommt allein ueber die Gruppe -- eine eigene Rolle hat
        // dieses Konto nicht.
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Der_erste_Administrator_darf_weiterhin_alles()
    {
        HttpClient client = CreateClient();
        await AnmeldenAsync(client, SouffleurApiFactory.AdminEmail, SouffleurApiFactory.AdminPassword);

        HttpResponseMessage response = await client.PutAsJsonAsync(
            "/api/organization",
            new { name = "Probebühne", legalForm = "e. V." });

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Die_mitgelieferten_Rollen_haben_ihre_Rechte_bekommen()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SouffleurDbContext>();

        foreach ((string name, Permission[] erwartet) in Roles.DefaultPermissions)
        {
            Role rolle = await context.Roles.SingleAsync(r => r.Name == name);
            List<Permission> tatsaechlich = await context.RolePermissions
                .Where(p => p.RoleId == rolle.Id)
                .Select(p => p.Permission)
                .ToListAsync();

            tatsaechlich.Should().BeEquivalentTo(erwartet, because: $"Rolle {name}");
        }
    }

    [Fact]
    public async Task Eine_mitgelieferte_Rolle_behaelt_ihren_Namen_und_bleibt()
    {
        HttpClient client = CreateClient();
        await AnmeldenAsync(client, SouffleurApiFactory.AdminEmail, SouffleurApiFactory.AdminPassword);

        Guid adminId;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SouffleurDbContext>();
            adminId = (await context.Roles.SingleAsync(r => r.Name == Roles.Administrator)).Id;
        }

        // Umbenennen wird still uebergangen -- sonst legte der naechste Start
        // eine zweite Administrator-Rolle mit allen Rechten an.
        HttpResponseMessage umbenannt = await client.PutAsJsonAsync(
            $"/api/access/roles/{adminId}",
            new { name = "Chefetage", description = "neu", permissions = Permissions.All });
        umbenannt.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage geloescht = await client.DeleteAsync($"/api/access/roles/{adminId}");
        geloescht.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SouffleurDbContext>();
            Role rolle = await context.Roles.SingleAsync(r => r.Id == adminId);
            rolle.Name.Should().Be(Roles.Administrator);
            rolle.Description.Should().Be("neu");
        }
    }

    [Fact]
    public async Task Ohne_das_Recht_bleibt_die_Verwaltung_der_Zugaenge_zu()
    {
        string email = await KontoAsync("neugierig", Permission.ModulesManage);
        HttpClient client = CreateClient();
        await AnmeldenAsync(client, email, Passwort);

        HttpResponseMessage response = await client.GetAsync("/api/access/users");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Ohne_Leserecht_bleibt_die_Ablage_zu()
    {
        string email = await KontoAsync("ohneablage");
        HttpClient client = CreateClient();
        await AnmeldenAsync(client, email, Passwort);

        (await client.GetAsync("/api/documents")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Mit_Leserecht_aber_ohne_Schreibrecht_wird_nichts_abgelegt()
    {
        string email = await KontoAsync("nurlesen", Permission.DocumentsRead);
        HttpClient client = CreateClient();
        await AnmeldenAsync(client, email, Passwort);

        (await client.GetAsync("/api/documents")).StatusCode.Should().Be(HttpStatusCode.OK);

        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent("%PDF-1.4"u8.ToArray()), "file", "a.pdf" },
        };
        (await client.PostAsync("/api/documents", form)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Zur_Vorschau_kommt_nur_inline_was_der_Browser_gefahrlos_zeigt()
    {
        string email = await KontoAsync("vorschau", Permission.DocumentsWrite);
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SouffleurDbContext>();
            Role rolle = await context.Roles.SingleAsync(r => r.Name == "Rolle-vorschau");
            context.RolePermissions.Add(new RolePermission { RoleId = rolle.Id, Permission = Permission.DocumentsRead });
            await context.SaveChangesAsync();
        }

        HttpClient client = CreateClient();
        await AnmeldenAsync(client, email, Passwort);

        async Task<Guid> AblegenAsync(byte[] inhalt, string name)
        {
            using var form = new MultipartFormDataContent { { new ByteArrayContent(inhalt), "file", name } };
            HttpResponseMessage antwort = await client.PostAsync("/api/documents", form);
            antwort.StatusCode.Should().Be(HttpStatusCode.Created);
            return (await antwort.Content.ReadFromJsonAsync<IdOnly>())!.Id;
        }

        Guid pdf = await AblegenAsync("%PDF-1.4 vorschau"u8.ToArray(), "programm.pdf");
        Guid text = await AblegenAsync("Probenplan"u8.ToArray(), "notiz.txt");
        Guid word = await AblegenAsync([0x50, 0x4B, 0x03, 0x04, 0x01], "vertrag.docx");

        HttpResponseMessage pdfAntwort = await client.GetAsync($"/api/documents/{pdf}/file?inline=true");
        pdfAntwort.Content.Headers.ContentDisposition!.DispositionType.Should().Be("inline");
        pdfAntwort.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        // Text ja, aber ohne Skripte.
        HttpResponseMessage textAntwort = await client.GetAsync($"/api/documents/{text}/file?inline=true");
        textAntwort.Content.Headers.ContentDisposition!.DispositionType.Should().Be("inline");
        textAntwort.Headers.GetValues("Content-Security-Policy").Single().Should().StartWith("sandbox");

        // Office laesst sich im Browser nicht zeigen -- es bleibt beim Anhang.
        HttpResponseMessage wordAntwort = await client.GetAsync($"/api/documents/{word}/file?inline=true");
        wordAntwort.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");

        // Ohne den Schalter wie immer.
        HttpResponseMessage download = await client.GetAsync($"/api/documents/{pdf}/file");
        download.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
    }

    [Fact]
    public async Task Eine_Erwaehnung_im_geschlossenen_Kanal_erreicht_niemanden_draussen()
    {
        string drinMail = await KontoAsync("imkanal");
        string draussenMail = await KontoAsync("draussen");

        Guid drin, draussen;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            drin = (await users.FindByEmailAsync(drinMail))!.Id;
            draussen = (await users.FindByEmailAsync(draussenMail))!.Id;
        }

        HttpClient admin = CreateClient();
        await AnmeldenAsync(admin, SouffleurApiFactory.AdminEmail, SouffleurApiFactory.AdminPassword);

        var kanal = await (await admin.PostAsJsonAsync("/api/chat/conversations", new
        {
            name = "personalrat",
            isPrivate = true,
            participants = new[] { drin },
        })).Content.ReadFromJsonAsync<IdOnly>();

        (await admin.PostAsJsonAsync($"/api/chat/conversations/{kanal!.Id}/messages", new
        {
            text = "Bitte vertraulich: die Abmahnung",
            mentions = new[] { drin, draussen },
        })).StatusCode.Should().Be(HttpStatusCode.Created);

        async Task<int> UngelesenAsync(string email)
        {
            HttpClient client = CreateClient();
            await AnmeldenAsync(client, email, Passwort);
            var antwort = await client.GetFromJsonAsync<UngelesenOnly>("/api/notifications");
            return antwort!.Unread;
        }

        (await UngelesenAsync(drinMail)).Should().Be(1);
        (await UngelesenAsync(draussenMail)).Should().Be(0, "wer nicht im Kanal ist, liest auch die Glocke dazu nicht");
    }

    private sealed record IdOnly(Guid Id);

    private sealed record UngelesenOnly(int Unread);
}
