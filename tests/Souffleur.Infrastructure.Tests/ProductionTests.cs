using Souffleur.Infrastructure.Accounting;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Souffleur.Domain.Accounting;
using Souffleur.Domain.Calendar;
using Souffleur.Domain.Contacts;
using Souffleur.Domain.Productions;
using Souffleur.Domain.Work;
using Souffleur.Infrastructure.Documents;
using Souffleur.Infrastructure.Modules;
using Souffleur.Infrastructure.Persistence;
using Souffleur.Infrastructure.Productions;

namespace Souffleur.Infrastructure.Tests;

/// <summary>
/// Die Produktion als Klammer: Sie fuehrt nichts selbst, liest aber zusammen,
/// was die anderen Module an sie geheftet haben -- und laesst sich nur
/// loeschen, solange nichts daran haengt.
/// </summary>
public class ProductionTests : AccountingTestBase
{
    private ProductionService Service
    {
        get
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Souffleur:DataDirectory"] = Path.Combine(Path.GetTempPath(), "souffleur-tests", Guid.NewGuid().ToString("N")),
                })
                .Build();

            var scopes = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
            return new ProductionService(Context, new ModuleRegistry(scopes), new DocumentService(Context, new DocumentStorage(configuration), new FolderStorage(configuration),
                new FolderService(Context, new FolderStorage(configuration),
                    new VoucherStorage(configuration, NullLogger<VoucherStorage>.Instance),
                    TimeProvider.System, NullLogger<FolderService>.Instance)));
        }
    }

    private static readonly DocumentViewer Alles = new(null, CanManage: true);

    private async Task<Contact> PersonAsync(string surname)
    {
        var contact = new Contact
        {
            Kind = ContactKind.Person,
            Name = $"{surname}, Lea",
            GivenName = "Lea",
            Surname = surname,
            Roles = [new ContactRoleAssignment { Role = ContactRole.Artist }],
        };
        Context.Contacts.Add(contact);
        await Context.SaveChangesAsync();
        return contact;
    }

    [Fact]
    public async Task Eine_Produktion_entsteht_mit_Rollen_besetzt_oder_offen()
    {
        Contact regie = await PersonAsync("Behrens");

        Production hamlet = await Service.AddAsync(
            new Production { Title = "Hamlet", Subtitle = "Shakespeare", Budget = 18_000m },
            new ProductionPlan(Roles:
            [
                new ProductionRoleInput("Regie", ContactId: regie.Id),
                new ProductionRoleInput("Ophelia", Note: "Vorsprechen am Freitag"),
            ]));

        Production? geladen = await Service.FindAsync(hamlet.Id);
        geladen!.Roles.Should().HaveCount(2);
        geladen.Roles[0].Contact!.Name.Should().Be("Behrens, Lea");
        geladen.Roles[1].IsCast.Should().BeFalse(because: "eine offene Rolle gehoert auf den Zettel");
        geladen.Status.Should().Be(ProductionStatus.Planned);

        ProductionFigures zahlen = (await Service.FiguresAsync([hamlet.Id], Alles))[hamlet.Id];
        zahlen.Roles.Should().Be(2);
        zahlen.OpenRoles.Should().Be(1);

        Func<Task> fremd = () => Service.AddAsync(new Production { Title = "Falsch" },
            new ProductionPlan(Roles: [new ProductionRoleInput("Regie", ContactId: Guid.NewGuid())]));
        (await fremd.Should().ThrowAsync<ProductionException>()).Which.Problems.Should().ContainMatch("*Adressbuch*");
    }

    [Fact]
    public async Task Die_Vorgaben_werden_abschnittsweise_ersetzt_und_der_Plan_summiert()
    {
        Production hamlet = await Service.AddAsync(new Production { Title = "Hamlet", Budget = 18_000m });

        await Service.UpdateAsync(hamlet.Id, _ => { }, new ProductionPlan(BudgetLines:
        [
            new ProductionBudgetLineInput("Gagen", 7_500m, "Gagen"),
            new ProductionBudgetLineInput("Bühne", 2_800m, "Ausstattung"),
        ]));

        await Service.UpdateAsync(hamlet.Id, _ => { }, new ProductionPlan(Needs:
        [
            new ProductionNeedInput("Degen", Quantity: "2 Paar"),
            new ProductionNeedInput("Aufführungsvertrag", ProductionNeedKind.Document, IsDone: true),
        ]));

        Production? geladen = await Service.FindAsync(hamlet.Id);
        geladen!.BudgetLines.Should().HaveCount(2, because: "der zweite Aufruf liess das Budget unberuehrt");
        geladen.PlannedTotal.Should().Be(10_300m);
        geladen.Needs.Should().HaveCount(2);

        ProductionFigures zahlen = (await Service.FiguresAsync([hamlet.Id], Alles))[hamlet.Id];
        zahlen.Planned.Should().Be(10_300m);
        zahlen.OpenNeeds.Should().Be(1);
        zahlen.Papers.Should().Be(1);
        zahlen.OpenPapers.Should().Be(0);

        // Ersetzen heisst ersetzen: eine Zeile weniger, keine Leiche.
        await Service.UpdateAsync(hamlet.Id, _ => { }, new ProductionPlan(BudgetLines: [new ProductionBudgetLineInput("Gagen", 7_500m)]));
        (await Service.FindAsync(hamlet.Id))!.BudgetLines.Should().ContainSingle();
        Context.ProductionBudgetLines.Count(l => l.ProductionId == hamlet.Id).Should().Be(1);
    }

    [Fact]
    public async Task Vom_Kontakt_aus_kommt_eine_Rolle_dazu_und_die_Besetzungen_sind_lesbar()
    {
        Contact clara = await PersonAsync("Weber");
        Production hamlet = await Service.AddAsync(new Production { Title = "Hamlet" });
        Production dame = await Service.AddAsync(new Production { Title = "Der Besuch der alten Dame", Status = ProductionStatus.Running });

        await Service.AddRoleAsync(hamlet.Id, new ProductionRoleInput("Hamlet", ContactId: clara.Id));
        ProductionRole claire = await Service.AddRoleAsync(dame.Id, new ProductionRoleInput("Claire Zachanassian", ContactId: clara.Id));

        List<Casting> besetzungen = await Service.CastingsAsync(clara.Id);
        besetzungen.Should().HaveCount(2);
        besetzungen.Select(b => b.Role.Name).Should().Contain(["Hamlet", "Claire Zachanassian"]);

        await Service.RemoveRoleAsync(dame.Id, claire.Id);
        (await Service.CastingsAsync(clara.Id)).Should().ContainSingle().Which.Production.Title.Should().Be("Hamlet");
    }

    [Fact]
    public async Task Derniere_vor_Premiere_und_negatives_Budget_werden_abgewiesen()
    {
        Func<Task> act = () => Service.AddAsync(new Production
        {
            Title = "Hamlet",
            PremiereDate = new DateOnly(2027, 3, 1),
            ClosingDate = new DateOnly(2027, 2, 1),
            Budget = -1,
        });

        (await act.Should().ThrowAsync<ProductionException>()).Which.Problems
            .Should().Contain("Die Derniere liegt vor der Premiere.")
            .And.Contain("Ein Budget ist nicht negativ.");
    }

    [Fact]
    public async Task Die_Zahlen_kommen_aus_den_anderen_Modulen()
    {
        Production hamlet = await Service.AddAsync(new Production { Title = "Hamlet" });

        Context.TaskItems.AddRange(
            new TaskItem { Title = "Bühnenbild abnehmen", ProductionId = hamlet.Id },
            new TaskItem { Title = "Pressetext", ProductionId = hamlet.Id, State = TaskState.Done },
            new TaskItem { Title = "Etwas anderes" });

        DateTimeOffset morgen = DateTimeOffset.UtcNow.AddDays(1);
        Context.CalendarEvents.AddRange(
            new CalendarEvent { Title = "Probe", StartsAt = morgen, EndsAt = morgen.AddHours(3), ProductionId = hamlet.Id },
            new CalendarEvent { Title = "Entfällt", StartsAt = morgen, EndsAt = morgen.AddHours(1), ProductionId = hamlet.Id, Status = EventStatus.Cancelled });

        Context.Vouchers.AddRange(
            new Voucher { Number = 1, FiscalYear = 2026, Date = new DateOnly(2026, 3, 1), GrossAmount = 1800m, Description = "Honorar Gastregie", Direction = VoucherDirection.Incoming, Source = VoucherSource.PaperOnly, ProductionId = hamlet.Id },
            new Voucher { Number = 2, FiscalYear = 2026, Date = new DateOnly(2026, 3, 2), GrossAmount = 500m, Description = "Gastspielgage", Direction = VoucherDirection.Outgoing, Source = VoucherSource.PaperOnly, ProductionId = hamlet.Id });
        await Context.SaveChangesAsync();

        ProductionOverview? overview = await Service.OverviewAsync(hamlet.Id, Alles);

        overview!.Figures.Tasks.Should().Be(2);
        overview.Figures.OpenTasks.Should().Be(1);
        overview.Figures.Events.Should().Be(1, because: "Abgesagtes zaehlt nicht");
        overview.Figures.NextEvent.Should().BeCloseTo(morgen, TimeSpan.FromSeconds(1));
        overview.Figures.Costs.Should().Be(1800m);
        overview.Figures.Income.Should().Be(500m);
        overview.Tasks.Should().HaveCount(2).And.Subject.First().Title.Should().Be("Bühnenbild abnehmen", because: "Offenes zuerst");
        overview.Vouchers.Should().HaveCount(2);
    }

    [Fact]
    public async Task Mit_Anhang_wird_nicht_geloescht_sondern_abgespielt()
    {
        Production hamlet = await Service.AddAsync(new Production { Title = "Hamlet" });
        Context.TaskItems.Add(new TaskItem { Title = "Bühnenbild abnehmen", ProductionId = hamlet.Id });
        await Context.SaveChangesAsync();

        Func<Task> act = () => Service.DeleteAsync(hamlet.Id);
        (await act.Should().ThrowAsync<ProductionException>()).Which.Problems.Should().ContainMatch("*1 Aufgabe*");

        Production leer = await Service.AddAsync(new Production { Title = "Nie gespielt" });
        await Service.DeleteAsync(leer.Id);
        (await Service.FindAsync(leer.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Ein_festgeschriebener_Beleg_nimmt_noch_seine_Produktion_an_aber_sonst_nichts()
    {
        Production hamlet = await Service.AddAsync(new Production { Title = "Hamlet" });

        var beleg = new Voucher
        {
            Number = 1, FiscalYear = 2026, Date = new DateOnly(2026, 3, 1), GrossAmount = 1800m,
            Description = "Honorar Gastregie", Direction = VoucherDirection.Incoming, Source = VoucherSource.PaperOnly,
        };
        beleg.Commit(DateTimeOffset.UtcNow);
        Context.Vouchers.Add(beleg);
        await Context.SaveChangesAsync();

        // Die Ordnung des Hauses darf nachgetragen werden ...
        await Service.AssignVoucherAsync(hamlet.Id, beleg.Id);
        (await Service.OverviewAsync(hamlet.Id, Alles))!.Figures.Costs.Should().Be(1800m);

        // ... die Belegangaben nicht.
        Voucher geladen = await Context.Vouchers.FindAsync(beleg.Id) ?? throw new InvalidOperationException();
        geladen.Description = "Anders";
        Func<Task> act = () => Context.SaveChangesAsync();
        await act.Should().ThrowAsync<ImmutableRecordException>();
    }
}
