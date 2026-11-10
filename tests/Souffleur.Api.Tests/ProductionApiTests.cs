using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Souffleur.Domain.Accounting;
using Souffleur.Domain.Identity;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Api.Tests;

/// <summary>
/// Die Produktionen ueber die Schnittstelle: Wer darf was, und was sieht
/// wer -- die Finanzzahlen folgen dem Finanzrecht, nicht dem Produktionsrecht.
/// </summary>
public class ProductionApiTests(SouffleurApiFactory factory) : IClassFixture<SouffleurApiFactory>
{
    private const string Passwort = "ProduktionTestPasswort123";

    private sealed record Figures(int Roles, int OpenRoles, int OpenTasks, int Tasks, int Events, int Documents, decimal Planned, decimal? Costs, decimal? Income, int Tours, int OpenNeeds, int OpenPapers);

    private sealed record ProductionDto(Guid Id, string Title, string? Subtitle, int Status, string StatusLabel, DateOnly? PremiereDate, decimal? Budget, List<RoleDto> Roles, List<BudgetLineDto> BudgetLines, Figures? Figures);

    private sealed record RoleDto(Guid Id, string Name, Guid? ContactId, string? ContactName, bool IsCast, Guid? FunctionId, string? FunctionLabel);

    private sealed record BudgetLineDto(Guid Id, string Title, string? Category, decimal Amount);

    private sealed record OverviewDto(ProductionDto Production, List<object> Tasks, List<object> Events, List<object>? Vouchers, List<object> Documents, List<object> Tours);

    private sealed record ModuleDto(int Module, string Key, string Label, bool IsEnabled);

    private HttpClient CreateClient() =>
        factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = true });

    private static async Task AnmeldenAsync(HttpClient client, string email, string passwort)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/account/login", new { email, password = passwort, rememberMe = false });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<HttpClient> AdminAsync()
    {
        HttpClient client = CreateClient();
        await AnmeldenAsync(client, SouffleurApiFactory.AdminEmail, SouffleurApiFactory.AdminPassword);
        return client;
    }

    private async Task<HttpClient> KontoAsync(string kennung, params Permission[] rechte)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var context = scope.ServiceProvider.GetRequiredService<SouffleurDbContext>();

        string email = $"{kennung}@test.local";
        var user = new User { UserName = email, Email = email, EmailConfirmed = true, GivenName = "Test", Surname = kennung };
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

    private static async Task<ProductionDto> AnlegenAsync(HttpClient client, string title, object? extra = null)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/productions", new
        {
            title,
            subtitle = "Probe",
            status = 2,
            premiereDate = "2027-03-12",
            budget = 18000,
            roles = Array.Empty<object>(),
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ProductionDto>())!;
    }

    private async Task<Guid> KontaktAsync(HttpClient client, string name)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/contacts", new
        {
            kind = 2, givenName = "Lea", surname = name, roles = new[] { 5 },
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Das_Modul_ist_in_einer_frischen_Installation_an()
    {
        HttpClient client = await AdminAsync();
        List<ModuleDto>? modules = await client.GetFromJsonAsync<List<ModuleDto>>("/api/modules");
        modules.Should().Contain(m => m.Key == "Production" && m.IsEnabled)
            .Which.Label.Should().Be("Produktionen");
    }

    [Fact]
    public async Task Anlegen_mit_Beteiligten_und_wieder_lesen()
    {
        HttpClient client = await AdminAsync();
        Guid regie = await KontaktAsync(client, "Behrens");

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/productions", new
        {
            title = "  Hamlet ",
            subtitle = "William Shakespeare",
            premiereDate = "2027-03-12",
            budget = 18000,
            roles = new[]
            {
                new { name = "Regie", contactId = (Guid?)regie, functionId = (Guid?)null, note = (string?)"Gastregie" },
                new { name = "Ophelia", contactId = (Guid?)null, functionId = (Guid?)null, note = (string?)null },
            },
            budgetLines = new[] { new { title = "Gagen", amount = 7500m, category = "Gagen", note = (string?)null } },
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        ProductionDto created = (await response.Content.ReadFromJsonAsync<ProductionDto>())!;
        created.Title.Should().Be("Hamlet");
        created.Roles.Should().HaveCount(2);
        created.Roles[0].ContactName.Should().Be("Behrens, Lea");
        created.Roles[1].IsCast.Should().BeFalse();
        created.BudgetLines.Should().ContainSingle().Which.Amount.Should().Be(7500m);

        List<ProductionDto>? liste = await client.GetFromJsonAsync<List<ProductionDto>>("/api/productions");
        ProductionDto hamlet = liste!.Single(p => p.Id == created.Id);
        hamlet.Figures.Should().NotBeNull();
        hamlet.Figures!.Roles.Should().Be(2);
        hamlet.Figures.OpenRoles.Should().Be(1);
        hamlet.Figures.Planned.Should().Be(7500m);
        hamlet.Figures.Costs.Should().Be(0m, because: "die Verwaltung sieht die Finanzen");

        OverviewDto? overview = await client.GetFromJsonAsync<OverviewDto>($"/api/productions/{created.Id}");
        overview!.Production.StatusLabel.Should().Be("In Planung");
        overview.Vouchers.Should().NotBeNull();
    }

    [Fact]
    public async Task Aufgaben_und_Belege_haengen_an_der_Produktion()
    {
        HttpClient client = await AdminAsync();
        ProductionDto hamlet = await AnlegenAsync(client, "Hamlet mit Anhang");

        HttpResponseMessage aufgabe = await client.PostAsJsonAsync("/api/tasks", new
        {
            title = "Bühnenbild abnehmen", productionId = hamlet.Id,
        });
        aufgabe.StatusCode.Should().Be(HttpStatusCode.Created, await aufgabe.Content.ReadAsStringAsync());
        (await aufgabe.Content.ReadAsStringAsync()).Should().Contain("\"productionTitle\":\"Hamlet mit Anhang\"");

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SouffleurDbContext>();
            context.Vouchers.Add(new Voucher
            {
                Number = 9001, FiscalYear = 2026, Date = new DateOnly(2026, 3, 1), GrossAmount = 1800m,
                Description = "Honorar Gastregie", Direction = VoucherDirection.Incoming,
                Source = VoucherSource.PaperOnly, ProductionId = hamlet.Id,
            });
            await context.SaveChangesAsync();
        }

        OverviewDto? overview = await client.GetFromJsonAsync<OverviewDto>($"/api/productions/{hamlet.Id}");
        overview!.Production.Figures!.OpenTasks.Should().Be(1);
        overview.Production.Figures.Costs.Should().Be(1800m);
        overview.Tasks.Should().HaveCount(1);
        overview.Vouchers.Should().HaveCount(1);

        // Mit Anhang wird nicht geloescht.
        HttpResponseMessage delete = await client.DeleteAsync($"/api/productions/{hamlet.Id}");
        delete.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await delete.Content.ReadAsStringAsync()).Should().Contain("abgespielt");

        // Eine fremde Produktion an der Aufgabe wird abgewiesen.
        HttpResponseMessage falsch = await client.PostAsJsonAsync("/api/tasks", new
        {
            title = "Irgendwas", productionId = Guid.NewGuid(),
        });
        falsch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Ohne_Finanzrecht_fehlen_die_Zahlen_ohne_Produktionsrecht_das_Anlegen()
    {
        HttpClient admin = await AdminAsync();
        ProductionDto hamlet = await AnlegenAsync(admin, "Hamlet ohne Zahlen");

        HttpClient mitarbeit = await KontoAsync("mitarbeit", Permission.DocumentsRead);

        List<ProductionDto>? liste = await mitarbeit.GetFromJsonAsync<List<ProductionDto>>("/api/productions");
        ProductionDto sicht = liste!.Single(p => p.Id == hamlet.Id);
        sicht.Figures!.Costs.Should().BeNull();
        sicht.Budget.Should().Be(18000m, because: "der Plan ist kein Beleg");

        OverviewDto? overview = await mitarbeit.GetFromJsonAsync<OverviewDto>($"/api/productions/{hamlet.Id}");
        overview!.Vouchers.Should().BeNull();

        HttpResponseMessage verboten = await mitarbeit.PostAsJsonAsync("/api/productions", new { title = "Verboten" });
        verboten.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        HttpClient disposition = await KontoAsync("dispo", Permission.ProductionsManage);
        HttpResponseMessage erlaubt = await disposition.PostAsJsonAsync("/api/productions", new { title = "Erlaubt" });
        erlaubt.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Touring_waehlt_aus_den_Produktionen()
    {
        HttpClient client = await AdminAsync();
        ProductionDto oratorium = await AnlegenAsync(client, "Oratorium");

        HttpResponseMessage an = await client.PutAsJsonAsync("/api/modules/Touring", new { isEnabled = true, note = (string?)null });
        an.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage tour = await client.PostAsJsonAsync("/api/touring/tours", new
        {
            title = "Herbsttournee", productionId = oratorium.Id,
        });
        tour.StatusCode.Should().Be(HttpStatusCode.Created, await tour.Content.ReadAsStringAsync());
        (await tour.Content.ReadAsStringAsync()).Should().Contain("\"productionLabel\":\"Oratorium\"");

        HttpResponseMessage fremd = await client.PostAsJsonAsync("/api/touring/tours", new
        {
            title = "Falsch", productionId = Guid.NewGuid(),
        });
        fremd.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        OverviewDto? overview = await client.GetFromJsonAsync<OverviewDto>($"/api/productions/{oratorium.Id}");
        overview!.Tours.Should().HaveCount(1);
    }

    [Fact]
    public async Task Die_Suche_findet_die_Produktion_und_springt_hin()
    {
        HttpClient client = await AdminAsync();
        ProductionDto stueck = await AnlegenAsync(client, "Woyzeck Suchprobe");

        string antwort = await client.GetStringAsync("/api/search?q=woyzeck%20suchprobe");
        antwort.Should().Contain("\"key\":\"produktionen\"");
        antwort.Should().Contain($"/produktionen?produktion={stueck.Id}");
    }

    [Fact]
    public async Task Abgeschaltet_antwortet_das_Modul_mit_404()
    {
        HttpClient client = await AdminAsync();
        try
        {
            HttpResponseMessage aus = await client.PutAsJsonAsync("/api/modules/Production", new { isEnabled = false, note = (string?)null });
            aus.StatusCode.Should().Be(HttpStatusCode.NoContent);

            HttpResponseMessage response = await client.GetAsync("/api/productions");
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            await client.PutAsJsonAsync("/api/modules/Production", new { isEnabled = true, note = (string?)null });
        }
    }
}
