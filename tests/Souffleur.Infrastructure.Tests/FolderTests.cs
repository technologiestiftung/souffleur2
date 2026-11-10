using System.Text;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Souffleur.Domain.Accounting;
using Souffleur.Domain.Documents;
using Souffleur.Domain.Identity;
using Souffleur.Domain.Modules;
using Souffleur.Domain.Productions;
using Souffleur.Infrastructure.Accounting;
using Souffleur.Infrastructure.Documents;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Tests;

/// <summary>
/// Die Ordner auf der Synology (ADR-0013): Was die File Station tut, muss
/// souffleur nachlesen, ohne zu vergessen, was es ueber die Dateien weiss.
/// </summary>
public class FolderTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly string daten = Path.Combine(Path.GetTempPath(), "souffleur-folder-tests", Guid.NewGuid().ToString("N"));

    private SouffleurDbContext context = null!;
    private IConfiguration configuration = null!;
    private FolderStorage storage = null!;
    private FolderService folders = null!;
    private DocumentService documents = null!;

    private string Ablage => Path.Combine(daten, "ablage");

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        context = new SouffleurDbContext(
            new DbContextOptionsBuilder<SouffleurDbContext>().UseSqlite(connection).Options,
            TimeProvider.System,
            new Niemand());
        await context.Database.EnsureCreatedAsync();

        configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Souffleur:DataDirectory"] = daten })
            .Build();

        storage = new FolderStorage(configuration);
        folders = new FolderService(
            context, storage, new VoucherStorage(configuration, NullLogger<VoucherStorage>.Instance),
            TimeProvider.System, NullLogger<FolderService>.Instance);
        documents = new DocumentService(context, new DocumentStorage(configuration), storage, folders);

        // Zwei Freigaben, wie die Synology sie einhaengt.
        Directory.CreateDirectory(Path.Combine(Ablage, "Verwaltung", "Finanzen"));
        Directory.CreateDirectory(Path.Combine(Ablage, "Verwaltung", "#recycle"));
        Directory.CreateDirectory(Path.Combine(Ablage, "Kunst", "Produktionen"));
    }

    public async Task DisposeAsync()
    {
        await context.DisposeAsync();
        await connection.DisposeAsync();
        if (Directory.Exists(daten))
        {
            Directory.Delete(daten, recursive: true);
        }
    }

    /// <summary>
    /// Eine Datei, wie sie die File Station hinterlaesst -- mit einer
    /// Aenderungszeit in der Vergangenheit, sonst hielte das Einlesen sie fuer
    /// eine, die gerade noch geschrieben wird.
    /// </summary>
    private string Lege(string relativ, string inhalt)
    {
        string pfad = Path.Combine(Ablage, relativ);
        Directory.CreateDirectory(Path.GetDirectoryName(pfad)!);
        File.WriteAllText(pfad, inhalt);
        File.SetLastWriteTimeUtc(pfad, DateTime.UtcNow.AddMinutes(-5));
        return pfad;
    }

    private Task<DocumentFolder> OrdnerAsync(
        string pfad, SouffleurModule modul, bool standard = true, bool eingeschraenkt = false, Guid[]? gruppen = null) =>
        folders.SaveAsync(null, new FolderInput(pfad.Split('/')[^1], pfad, modul, standard, eingeschraenkt, gruppen ?? []));

    private Task<List<Document>> ImOrdnerAsync(DocumentFolder folder) =>
        context.Documents.Include(d => d.Tags).Include(d => d.Groups)
            .Where(d => d.FolderId == folder.Id).AsNoTracking().ToListAsync();

    [Theory]
    [InlineData("../data")]
    [InlineData("Kunst/../../etc")]
    [InlineData("Verwaltung/../../data")]
    public async Task Ein_Ordner_muss_unter_der_Ablage_liegen(string pfad)
    {
        Func<Task> eintragen = () => OrdnerAsync(pfad, SouffleurModule.Documents);

        await eintragen.Should().ThrowAsync<DocumentException>().WithMessage("*unterhalb der Ablage*");
    }

    [Fact]
    public async Task Einen_Ordner_den_es_nicht_gibt_nimmt_souffleur_nicht()
    {
        Func<Task> eintragen = () => OrdnerAsync("Verwaltung/Gibtsnicht", SouffleurModule.Finance);

        await eintragen.Should().ThrowAsync<DocumentException>().WithMessage("*gibt es auf der Synology nicht*");
    }

    [Fact]
    public async Task Ineinander_liegende_Ordner_gehen_nicht()
    {
        await OrdnerAsync("Verwaltung", SouffleurModule.Documents);

        Func<Task> drin = () => OrdnerAsync("Verwaltung/Finanzen", SouffleurModule.Finance);

        await drin.Should().ThrowAsync<DocumentException>().WithMessage("*ineinander*");
    }

    [Fact]
    public async Task Je_Modul_gibt_es_einen_Standard()
    {
        DocumentFolder erster = await OrdnerAsync("Kunst/Produktionen", SouffleurModule.Production);
        Directory.CreateDirectory(Path.Combine(Ablage, "Kunst", "Archiv"));
        DocumentFolder zweiter = await OrdnerAsync("Kunst/Archiv", SouffleurModule.Production);

        List<DocumentFolder> alle = await folders.ListAsync();
        alle.Single(f => f.Id == erster.Id).IsDefault.Should().BeFalse();
        alle.Single(f => f.Id == zweiter.Id).IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task Das_Einlesen_findet_Dateien_und_uebergeht_was_der_Synology_gehoert()
    {
        DocumentFolder folder = await OrdnerAsync("Kunst/Produktionen", SouffleurModule.Production);
        Lege("Kunst/Produktionen/Hamlet/Textbuch.pdf", "Sein oder nicht sein");
        Lege("Kunst/Produktionen/Hamlet/@eaDir/Textbuch.pdf/SYNOPHOTO_THUMB.jpg", "Vorschau");
        Lege("Kunst/Produktionen/.DS_Store", "x");
        Lege("Kunst/Produktionen/~$Probenplan.docx", "Sperrdatei");

        string ergebnis = await folders.ScanAsync(folder.Id);

        ergebnis.Should().Be("1 neu");
        Document textbuch = (await ImOrdnerAsync(folder)).Should().ContainSingle().Subject;
        textbuch.Name.Should().Be("Textbuch");
        textbuch.FolderPath.Should().Be("Hamlet/Textbuch.pdf");
        textbuch.ContentType.Should().Be("application/pdf");
    }

    [Fact]
    public async Task Eine_Datei_die_gerade_geschrieben_wird_wartet_auf_den_naechsten_Durchlauf()
    {
        DocumentFolder folder = await OrdnerAsync("Kunst/Produktionen", SouffleurModule.Production);
        File.WriteAllText(Path.Combine(Ablage, "Kunst/Produktionen/frisch.pdf"), "noch im Schreiben");

        await folders.ScanAsync(folder.Id);

        (await ImOrdnerAsync(folder)).Should().BeEmpty();
    }

    [Fact]
    public async Task Umbenennen_in_der_File_Station_behaelt_Schlagworte_und_Verknuepfungen()
    {
        DocumentFolder folder = await OrdnerAsync("Kunst/Produktionen", SouffleurModule.Production);
        string alt = Lege("Kunst/Produktionen/Entwurf.pdf", "Bühnenbild");
        await folders.ScanAsync(folder.Id);
        Document doc = (await ImOrdnerAsync(folder)).Single();
        context.DocumentTags.Add(new DocumentTag { DocumentId = doc.Id, Tag = "bühnenbild" });
        await context.SaveChangesAsync();

        string neu = Path.Combine(Ablage, "Kunst/Produktionen/Hamlet/Bühnenbild final.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(neu)!);
        File.Move(alt, neu);

        string ergebnis = await folders.ScanAsync(folder.Id);

        ergebnis.Should().Be("1 umbenannt oder verschoben");
        Document danach = (await ImOrdnerAsync(folder)).Should().ContainSingle().Subject;
        danach.Id.Should().Be(doc.Id);
        danach.FolderPath.Should().Be("Hamlet/Bühnenbild final.pdf");
        danach.Tags.Should().ContainSingle(t => t.Tag == "bühnenbild");
    }

    [Fact]
    public async Task Verschieben_in_einen_anderen_Ordner_nimmt_das_Dokument_mit()
    {
        DocumentFolder kunst = await OrdnerAsync("Kunst/Produktionen", SouffleurModule.Production);
        DocumentFolder finanzen = await OrdnerAsync("Verwaltung/Finanzen", SouffleurModule.Finance);
        string alt = Lege("Kunst/Produktionen/Förderbescheid.pdf", "Bescheid über 20.000 Euro");
        await folders.ScanAsync(kunst.Id);
        Guid id = (await ImOrdnerAsync(kunst)).Single().Id;

        File.Move(alt, Path.Combine(Ablage, "Verwaltung/Finanzen/Förderbescheid.pdf"));
        await folders.ScanAsync(kunst.Id);
        await folders.ScanAsync(finanzen.Id);

        (await ImOrdnerAsync(kunst)).Should().BeEmpty();
        (await ImOrdnerAsync(finanzen)).Should().ContainSingle(d => d.Id == id && d.MissingSince == null);
    }

    [Fact]
    public async Task Was_fehlt_bleibt_stehen_und_kommt_wieder()
    {
        DocumentFolder folder = await OrdnerAsync("Kunst/Produktionen", SouffleurModule.Production);
        string pfad = Lege("Kunst/Produktionen/Plakat.pdf", "Plakat");
        await folders.ScanAsync(folder.Id);

        string inhalt = File.ReadAllText(pfad);
        File.Delete(pfad);
        (await folders.ScanAsync(folder.Id)).Should().Be("1 fehlen");
        (await ImOrdnerAsync(folder)).Single().MissingSince.Should().NotBeNull();

        File.WriteAllText(pfad, inhalt);
        File.SetLastWriteTimeUtc(pfad, DateTime.UtcNow.AddMinutes(-5));
        (await folders.ScanAsync(folder.Id)).Should().Contain("wieder da");
        (await ImOrdnerAsync(folder)).Single().MissingSince.Should().BeNull();
    }

    [Fact]
    public async Task Eine_geaenderte_Datei_bekommt_einen_neuen_Pruefwert()
    {
        DocumentFolder folder = await OrdnerAsync("Kunst/Produktionen", SouffleurModule.Production);
        string pfad = Lege("Kunst/Produktionen/Probenplan.xlsx", "Woche 1");
        await folders.ScanAsync(folder.Id);
        string vorher = (await ImOrdnerAsync(folder)).Single().Sha256;

        File.WriteAllText(pfad, "Woche 1 und 2");
        File.SetLastWriteTimeUtc(pfad, DateTime.UtcNow.AddMinutes(-1));
        (await folders.ScanAsync(folder.Id)).Should().Be("1 geändert");

        (await ImOrdnerAsync(folder)).Single().Sha256.Should().NotBe(vorher);
    }

    [Fact]
    public async Task Ein_eingeschraenkter_Ordner_gibt_seine_Gruppen_weiter()
    {
        var verwaltung = new Group { Name = "Verwaltung" };
        context.Groups.Add(verwaltung);
        await context.SaveChangesAsync();
        DocumentFolder folder = await OrdnerAsync(
            "Verwaltung/Finanzen", SouffleurModule.Finance, eingeschraenkt: true, gruppen: [verwaltung.Id]);
        Lege("Verwaltung/Finanzen/Jahresabschluss.pdf", "Bilanz");

        await folders.ScanAsync(folder.Id);

        Document doc = (await ImOrdnerAsync(folder)).Single();
        doc.IsRestricted.Should().BeTrue();
        doc.Groups.Should().ContainSingle(g => g.GroupId == verwaltung.Id);
    }

    [Fact]
    public async Task Hochladen_an_einer_Produktion_landet_lesbar_in_ihrem_Ordner()
    {
        await OrdnerAsync("Kunst/Produktionen", SouffleurModule.Production);
        var hamlet = new Production { Title = "Hamlet" };
        context.Productions.Add(hamlet);
        await context.SaveChangesAsync();

        Document erstes = await documents.UploadAsync(
            new MemoryStream(Encoding.UTF8.GetBytes("Strichfassung")), "Strichfassung.pdf",
            new DocumentInput(null, null, [], [], false), [(DocumentTarget.Production, hamlet.Id)]);
        Document zweites = await documents.UploadAsync(
            new MemoryStream(Encoding.UTF8.GetBytes("Strichfassung neu")), "Strichfassung.pdf",
            new DocumentInput(null, null, [], [], false), [(DocumentTarget.Production, hamlet.Id)]);

        erstes.FolderPath.Should().Be("Hamlet/Strichfassung.pdf");
        zweites.FolderPath.Should().Be("Hamlet/Strichfassung (2).pdf");
        File.Exists(Path.Combine(Ablage, "Kunst/Produktionen/Hamlet/Strichfassung (2).pdf")).Should().BeTrue();
    }

    [Fact]
    public async Task Ohne_Ordner_bleibt_alles_in_der_eigenen_Ablage()
    {
        Document doc = await documents.UploadAsync(
            new MemoryStream(Encoding.UTF8.GetBytes("Notiz")), "notiz.pdf",
            new DocumentInput(null, null, [], [], false), []);

        doc.FolderId.Should().BeNull();
        doc.StoredAs.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Was_souffleur_loescht_landet_im_Papierkorb_der_Freigabe()
    {
        DocumentFolder folder = await OrdnerAsync("Verwaltung/Finanzen", SouffleurModule.Finance);
        Lege("Verwaltung/Finanzen/alt.pdf", "veraltet");
        await folders.ScanAsync(folder.Id);
        Document doc = (await ImOrdnerAsync(folder)).Single();

        (await documents.DeleteAsync(doc.Id, new DocumentViewer(null, CanManage: true))).Should().BeTrue();

        File.Exists(Path.Combine(Ablage, "Verwaltung/Finanzen/alt.pdf")).Should().BeFalse();
        Directory.EnumerateFiles(Path.Combine(Ablage, "Verwaltung/#recycle"), "alt.pdf", SearchOption.AllDirectories)
            .Should().ContainSingle();
    }

    [Fact]
    public async Task Ein_Beleg_kommt_als_eingeschraenkte_Kopie_in_den_Finanzordner()
    {
        DocumentFolder folder = await OrdnerAsync("Verwaltung/Finanzen", SouffleurModule.Finance);
        var belege = new VoucherService(
            context, new VoucherStorage(configuration, NullLogger<VoucherStorage>.Instance), TimeProvider.System);
        Voucher beleg = await belege.AddAsync(
            new Voucher
            {
                FiscalYear = 2026,
                Date = new DateOnly(2026, 3, 15),
                GrossAmount = 119m,
                Description = "Stromrechnung März",
                Direction = VoucherDirection.Incoming,
                Source = VoucherSource.Upload,
            },
            new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.4 Strom")), "strom.pdf", "application/pdf");

        (await folders.CopyVoucherAsync(beleg.Id)).Should().BeTrue();
        (await folders.CopyVoucherAsync(beleg.Id)).Should().BeFalse("eine Kopie reicht");

        Document kopie = (await context.Documents.Include(d => d.Links)
            .Where(d => d.FolderId == folder.Id).ToListAsync()).Should().ContainSingle().Subject;
        kopie.FolderPath.Should().Be("Belege/2026/2026-00001 – Stromrechnung März.pdf");
        kopie.IsRestricted.Should().BeTrue();
        kopie.Links.Should().ContainSingle(l => l.Target == DocumentTarget.Voucher && l.TargetId == beleg.Id);

        // Das Einlesen danach haelt die Kopie nicht fuer eine neue Datei.
        File.SetLastWriteTimeUtc(Path.Combine(Ablage, "Verwaltung/Finanzen", kopie.FolderPath!), DateTime.UtcNow.AddMinutes(-5));
        (await folders.ScanAsync(folder.Id)).Should().NotContain("neu");
    }

    private sealed class Niemand : ICurrentUser
    {
        public Guid? Id => null;
    }
}
