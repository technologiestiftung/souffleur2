using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Souffleur.Infrastructure.Mail;

namespace Souffleur.Api.Tests;

/// <summary>Wie im Haus ohne Mailserver, aber mit Protokoll-Versand: Der Ablauf laesst sich pruefen, nichts geht raus.</summary>
public class MailApiFactory : SouffleurApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Souffleur:Mail:LogOnly"] = "true",
            }));
    }
}

/// <summary>
/// E-Mail aus der Kommunikationsleiste. Geprueft wird, dass Empfaenger aus
/// Kontakten kommen, dass Angekommenes am Kontakt haengt und dass nichts
/// zweimal landet.
/// </summary>
public class MailApiTests(MailApiFactory factory) : IClassFixture<MailApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Status(bool CanSend, string SendMode, bool CanReceive, string? From);

    private sealed record Empfaenger(string Address, string? Name, Guid? ContactId);

    private sealed record Mail(
        Guid Id, string Direction, string FromAddress, string? FromName, Guid? ContactId, string? SenderName,
        List<Empfaenger> Recipients, string Subject, string Body, DateTimeOffset SentAt, DateTimeOffset? ReadAt, bool IsUnread);

    private sealed record Liste(int Unread, List<Mail> Mails);

    private async Task<HttpClient> AdminAsync()
    {
        HttpClient client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = true });
        HttpResponseMessage login = await client.PostAsJsonAsync(
            "/api/account/login",
            new { email = SouffleurApiFactory.AdminEmail, password = SouffleurApiFactory.AdminPassword, rememberMe = false });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        return client;
    }

    private static async Task<Guid> KontaktAsync(HttpClient client, string vorname, string nachname, string? email)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/contacts", new
        {
            kind = 2,
            roles = new[] { 1 },
            givenName = vorname,
            surname = nachname,
            email,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, because: await response.Content.ReadAsStringAsync());
        using JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Ohne_Mailserver_aber_mit_Protokoll_laesst_sich_senden_nicht_empfangen()
    {
        HttpClient client = await AdminAsync();

        Status? status = await client.GetFromJsonAsync<Status>("/api/mail/status", Json);

        status.Should().NotBeNull();
        status!.CanSend.Should().BeTrue();
        status.SendMode.Should().Be("Log");
        status.CanReceive.Should().BeFalse();
    }

    [Fact]
    public async Task Eine_Mail_an_einen_Kontakt_landet_mit_seiner_Adresse_in_der_Liste()
    {
        HttpClient client = await AdminAsync();
        Guid kontakt = await KontaktAsync(client, "Mira", "Senden", "mira.senden@example.org");

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/mail", new
        {
            recipients = new object[] { new { contactId = kontakt }, new { address = "zweite@example.org" } },
            subject = "Probe am Donnerstag",
            body = "Wir beginnen um 18 Uhr.",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created, because: await response.Content.ReadAsStringAsync());
        Mail? gesendet = await response.Content.ReadFromJsonAsync<Mail>(Json);
        gesendet!.Direction.Should().Be("outgoing");
        gesendet.Recipients.Should().HaveCount(2);
        gesendet.Recipients[0].Should().BeEquivalentTo(new Empfaenger("mira.senden@example.org", "Senden, Mira", kontakt));
        gesendet.Recipients[1].ContactId.Should().BeNull();
        gesendet.SenderName.Should().Be("Test Admin");
        gesendet.IsUnread.Should().BeFalse();

        Liste? liste = await client.GetFromJsonAsync<Liste>("/api/mail", Json);
        liste!.Mails.Should().Contain(m => m.Id == gesendet.Id && m.Subject == "Probe am Donnerstag");
    }

    [Fact]
    public async Task Ein_Kontakt_ohne_Adresse_und_ein_leerer_Betreff_werden_abgewiesen()
    {
        HttpClient client = await AdminAsync();
        Guid ohne = await KontaktAsync(client, "Ole", "Ohneadresse", null);

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/mail", new
        {
            recipients = new object[] { new { contactId = ohne } },
            subject = "",
            body = "Text",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        string text = await response.Content.ReadAsStringAsync();
        text.Should().Contain("keine E-Mail-Adresse hinterlegt");
    }

    [Fact]
    public async Task Angekommene_Mails_haengen_am_Kontakt_zaehlen_als_ungelesen_und_landen_nur_einmal()
    {
        HttpClient client = await AdminAsync();
        Guid kontakt = await KontaktAsync(client, "Paula", "Posteingang", "Paula.Posteingang@Example.org");

        IncomingMail[] post =
        [
            new(101, "<eins@example.org>", "paula.posteingang@example.org", "Paula Posteingang", "Frage zur Probe", "Wann geht es los?", DateTimeOffset.UtcNow.AddMinutes(-10), [("buero@haus.example", null)]),
            new(102, "<zwei@example.org>", "unbekannt@example.org", null, "Newsletter", "Angebote der Woche", DateTimeOffset.UtcNow.AddMinutes(-5), [("buero@haus.example", null)]),
        ];

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            var mails = scope.ServiceProvider.GetRequiredService<MailService>();
            (await mails.StoreIncomingAsync(post)).Should().Be(2);

            // Noch einmal dieselben: nichts Neues.
            (await mails.StoreIncomingAsync(post)).Should().Be(0);
            (await mails.LastImapUidAsync()).Should().Be(102);
        }

        Liste? liste = await client.GetFromJsonAsync<Liste>("/api/mail", Json);
        Mail frage = liste!.Mails.Single(m => m.Subject == "Frage zur Probe");
        frage.Direction.Should().Be("incoming");
        frage.ContactId.Should().Be(kontakt, because: "die Adresse passt bis auf Gross- und Kleinschreibung");
        frage.IsUnread.Should().BeTrue();
        liste.Mails.Single(m => m.Subject == "Newsletter").ContactId.Should().BeNull();
        liste.Unread.Should().BeGreaterThanOrEqualTo(2);

        HttpResponseMessage gelesen = await client.PostAsync($"/api/mail/{frage.Id}/read", null);
        gelesen.StatusCode.Should().Be(HttpStatusCode.OK);
        (await gelesen.Content.ReadFromJsonAsync<Mail>(Json))!.IsUnread.Should().BeFalse();
    }

    [Fact]
    public async Task Ohne_Anmeldung_gibt_es_keine_Post()
    {
        HttpClient anonym = factory.CreateClient();
        (await anonym.GetAsync("/api/mail")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
