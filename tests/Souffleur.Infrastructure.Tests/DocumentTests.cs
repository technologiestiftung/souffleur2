using Souffleur.Infrastructure.Accounting;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Souffleur.Domain.Contacts;
using Souffleur.Domain.Documents;
using Souffleur.Domain.Identity;
using Souffleur.Infrastructure.Documents;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Tests;

/// <summary>
/// Die Ablage. Das Wichtigste daran ist, wer was nicht sieht.
/// </summary>
public class DocumentTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly string ablage = Path.Combine(Path.GetTempPath(), "souffleur-doc-tests", Guid.NewGuid().ToString("N"));
    private readonly Guid anna = Guid.CreateVersion7();
    private readonly Guid ben = Guid.CreateVersion7();
    private readonly WechselnderNutzer wer = new();

    private SouffleurDbContext context = null!;
    private DocumentService service = null!;

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        context = new SouffleurDbContext(
            new DbContextOptionsBuilder<SouffleurDbContext>().UseSqlite(connection).Options,
            TimeProvider.System,
            wer);
        await context.Database.EnsureCreatedAsync();

        context.Users.AddRange(
            new User { Id = anna, GivenName = "Anna", Surname = "Krause", UserName = "anna" },
            new User { Id = ben, GivenName = "Ben", Surname = "Schröder", UserName = "ben" });
        await context.SaveChangesAsync();

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Souffleur:DataDirectory"] = ablage })
            .Build();
        service = new DocumentService(context, new DocumentStorage(configuration), new FolderStorage(configuration),
                new FolderService(context, new FolderStorage(configuration),
                    new VoucherStorage(configuration, NullLogger<VoucherStorage>.Instance),
                    TimeProvider.System, NullLogger<FolderService>.Instance));
    }

    public async Task DisposeAsync()
    {
        await context.DisposeAsync();
        await connection.DisposeAsync();
        if (Directory.Exists(ablage))
        {
            Directory.Delete(ablage, recursive: true);
        }
    }

    private static DocumentViewer Als(Guid userId) => new(userId, CanManage: false);

    private async Task<Group> GruppeAsync(string name, params Guid[] mitglieder)
    {
        var gruppe = new Group { Name = name };
        context.Groups.Add(gruppe);
        foreach (Guid id in mitglieder)
        {
            context.GroupMembers.Add(new GroupMember { GroupId = gruppe.Id, UserId = id });
        }

        await context.SaveChangesAsync();
        return gruppe;
    }

    private async Task<Document> AblegenAsync(
        Guid von,
        string datei = "vertrag.pdf",
        IReadOnlyCollection<Guid>? gruppen = null,
        IReadOnlyCollection<string>? tags = null,
        IReadOnlyCollection<(DocumentTarget, Guid)>? links = null)
    {
        wer.Id = von;
        using var inhalt = new MemoryStream(Encoding.UTF8.GetBytes($"%PDF-1.4 {Guid.NewGuid()}"));
        return await service.UploadAsync(
            inhalt,
            datei,
            new DocumentInput(null, null, tags ?? [], gruppen ?? [], Restricted: gruppen is { Count: > 0 }),
            links ?? []);
    }

    [Fact]
    public async Task Ein_offenes_Dokument_sehen_alle()
    {
        await AblegenAsync(anna);

        (await service.ListAsync(Als(ben))).Should().ContainSingle();
    }

    [Fact]
    public async Task Ein_eingeschraenktes_sehen_nur_die_Gruppe_und_wer_es_abgelegt_hat()
    {
        Group personal = await GruppeAsync("Personal", anna);
        Guid clara = Guid.CreateVersion7();
        context.Users.Add(new User { Id = clara, GivenName = "Clara", Surname = "Weber", UserName = "clara" });
        await context.SaveChangesAsync();

        await AblegenAsync(ben, gruppen: [personal.Id]);

        (await service.ListAsync(Als(anna))).Should().ContainSingle("Anna ist in der Gruppe");
        (await service.ListAsync(Als(ben))).Should().ContainSingle("Ben hat es abgelegt");
        (await service.ListAsync(Als(clara))).Should().BeEmpty("Clara ist weder das eine noch das andere");
        (await service.ListAsync(new DocumentViewer(clara, CanManage: true))).Should().ContainSingle(
            "wer fremde Dokumente verwaltet, sieht alles");
    }

    [Fact]
    public async Task Wird_die_Gruppe_geloescht_bleibt_das_Dokument_eingeschraenkt()
    {
        Group personal = await GruppeAsync("Personalakten", anna);
        await AblegenAsync(anna, gruppen: [personal.Id]);
        Guid clara = Guid.CreateVersion7();
        context.Users.Add(new User { Id = clara, GivenName = "Clara", Surname = "Weber", UserName = "clara" });
        context.Groups.Remove(personal);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // Das ist der Grund fuer das eigene Feld: Ohne Gruppe waere es sonst offen.
        (await service.ListAsync(Als(clara))).Should().BeEmpty();
        (await service.ListAsync(Als(anna))).Should().ContainSingle("sie hat es abgelegt");
    }

    [Fact]
    public async Task Was_man_nicht_sieht_laesst_sich_auch_nicht_finden_oder_loeschen()
    {
        Group personal = await GruppeAsync("Personal", anna);
        Document doc = await AblegenAsync(anna, gruppen: [personal.Id]);

        (await service.FindAsync(doc.Id, Als(ben))).Should().BeNull();
        (await service.DeleteAsync(doc.Id, Als(ben))).Should().BeFalse();
    }

    [Fact]
    public async Task Ein_fremdes_offenes_Dokument_sieht_man_aendert_es_aber_nicht()
    {
        Document doc = await AblegenAsync(anna);

        bool geaendert = await service.UpdateAsync(
            doc.Id, new DocumentInput("Umbenannt", null, [], [], false), Als(ben));

        geaendert.Should().BeFalse();
    }

    [Fact]
    public async Task Schlagworte_werden_vereinheitlicht_und_beim_Aendern_ersetzt()
    {
        Document doc = await AblegenAsync(anna, tags: ["Vertrag", " vertrag ", "Gastspiel"]);
        context.ChangeTracker.Clear();

        (await service.FindAsync(doc.Id, Als(anna)))!.Tags.Select(t => t.Tag)
            .Should().BeEquivalentTo(["vertrag", "gastspiel"]);

        wer.Id = anna;
        (await service.UpdateAsync(doc.Id, new DocumentInput(null, null, ["Abrechnung"], [], false), Als(anna)))
            .Should().BeTrue();
        context.ChangeTracker.Clear();

        (await service.FindAsync(doc.Id, Als(anna)))!.Tags.Select(t => t.Tag)
            .Should().BeEquivalentTo(["abrechnung"]);
        (await service.ListAsync(Als(anna), tag: "ABRECHNUNG")).Should().ContainSingle();
    }

    [Fact]
    public async Task Ein_Dokument_haengt_an_einem_Kontakt_und_ist_dort_zu_finden()
    {
        var kontakt = new Contact
        {
            Kind = ContactKind.Organization,
            Name = "Gastspielhaus",
            Roles = [new ContactRoleAssignment { Role = ContactRole.Supplier }],
        };
        context.Contacts.Add(kontakt);
        await context.SaveChangesAsync();

        await AblegenAsync(anna, links: [(DocumentTarget.Contact, kontakt.Id)]);
        await AblegenAsync(anna, datei: "anderes.pdf");

        (await service.ListAsync(Als(anna), target: DocumentTarget.Contact, targetId: kontakt.Id))
            .Should().ContainSingle();
    }

    [Fact]
    public async Task Eine_Verknuepfung_ins_Leere_wird_abgewiesen()
    {
        Func<Task> ablegen = () => AblegenAsync(anna, links: [(DocumentTarget.Task, Guid.NewGuid())]);

        await ablegen.Should().ThrowAsync<DocumentException>().WithMessage("*Aufgabe gibt es nicht*");
    }

    [Theory]
    [InlineData("seite.html")]
    [InlineData("bild.svg")]
    [InlineData("skript.js")]
    [InlineData("ohne-endung")]
    public async Task Was_der_Browser_ausfuehren_wuerde_kommt_nicht_hinein(string datei)
    {
        Func<Task> ablegen = () => AblegenAsync(anna, datei: datei);

        await ablegen.Should().ThrowAsync<DocumentFileException>();
        Directory.Exists(ablage).Should().BeFalse("es darf nichts liegen bleiben");
    }

    [Fact]
    public async Task Beim_Loeschen_geht_die_Datei_mit()
    {
        Document doc = await AblegenAsync(anna);
        string pfad = Path.Combine(ablage, "documents", doc.StoredAs);
        File.Exists(pfad).Should().BeTrue();

        (await service.DeleteAsync(doc.Id, Als(anna))).Should().BeTrue();

        File.Exists(pfad).Should().BeFalse();
    }

    private sealed class WechselnderNutzer : ICurrentUser
    {
        public Guid? Id { get; set; }
    }
}
