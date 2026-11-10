using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Souffleur.Domain.Accounting;
using Souffleur.Infrastructure.Accounting;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Tests;

/// <summary>
/// Belege und ihre Dateien. Die Datei ist der Teil, der acht Jahre überdauern
/// muss -- deshalb liegt der Schwerpunkt auf Ablage, Prüfwert und Unversehrtheit.
/// </summary>
public class VoucherTests : AccountingTestBase, IDisposable
{
    private readonly string dataDirectory =
        Path.Combine(Path.GetTempPath(), "souffleur-voucher-tests", Guid.NewGuid().ToString("N"));

    private VoucherStorage Storage => new(
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Souffleur:DataDirectory"] = dataDirectory })
            .Build(),
        NullLogger<VoucherStorage>.Instance);

    private VoucherService Service => new(Context, Storage, TimeProvider.System);

    public void Dispose()
    {
        if (Directory.Exists(dataDirectory))
        {
            Directory.Delete(dataDirectory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private static Voucher Invoice(decimal gross = 238m, int year = 2026) => new()
    {
        FiscalYear = year,
        Date = new DateOnly(year, 3, 15),
        GrossAmount = gross,
        Description = "Stromrechnung März",
        Direction = VoucherDirection.Incoming,
        Source = VoucherSource.Upload,
    };

    private static Stream Pdf(string content = "%PDF-1.4 Testbeleg") =>
        new MemoryStream(Encoding.UTF8.GetBytes(content));

    [Fact]
    public async Task Ein_Beleg_mit_Datei_wird_aufgenommen()
    {
        Voucher voucher = await Service.AddAsync(Invoice(), Pdf(), "rechnung.pdf", "application/pdf");

        voucher.Number.Should().Be(1);
        voucher.Label.Should().Be("2026-00001");
        voucher.File.Should().NotBeNull();
        voucher.File!.Sha256.Should().HaveLength(64);
        voucher.File.ContentType.Should().Be("application/pdf");
    }

    [Fact]
    public async Task Die_Datei_liegt_wirklich_auf_der_Platte()
    {
        Voucher voucher = await Service.AddAsync(Invoice(), Pdf(), "rechnung.pdf", "application/pdf");

        Storage.Exists(voucher.File!).Should().BeTrue();
        (await Storage.VerifyAsync(voucher.File!)).Should().BeTrue();
    }

    [Fact]
    public async Task Die_Belegnummer_laeuft_lueckenlos_je_Jahr()
    {
        await Service.AddAsync(Invoice(year: 2026), Pdf(), "a.pdf", "application/pdf");
        await Service.AddAsync(Invoice(year: 2026), Pdf("zwei"), "b.pdf", "application/pdf");
        Voucher newYear = await Service.AddAsync(Invoice(year: 2027), Pdf("drei"), "c.pdf", "application/pdf");

        Context.Vouchers.Count(v => v.FiscalYear == 2026).Should().Be(2);
        newYear.Number.Should().Be(1);
    }

    [Fact]
    public async Task Ein_veraenderter_Beleg_faellt_bei_der_Pruefung_auf()
    {
        Voucher voucher = await Service.AddAsync(Invoice(), Pdf(), "rechnung.pdf", "application/pdf");

        // Jemand schreibt die Datei im Dateisystem um -- genau das soll auffallen.
        string path = Directory.GetFiles(dataDirectory, "*.pdf", SearchOption.AllDirectories).Single();
        await File.WriteAllTextAsync(path, "etwas ganz anderes");

        (await Storage.VerifyAsync(voucher.File!)).Should().BeFalse();

        IReadOnlyList<VoucherIntegrityProblem> problems = await Service.CheckFilesAsync();
        problems.Should().ContainSingle()
            .Which.Problem.Should().Contain("weicht");
    }

    [Fact]
    public async Task Eine_fehlende_Datei_faellt_bei_der_Pruefung_auf()
    {
        await Service.AddAsync(Invoice(), Pdf(), "rechnung.pdf", "application/pdf");

        File.Delete(Directory.GetFiles(dataDirectory, "*.pdf", SearchOption.AllDirectories).Single());

        IReadOnlyList<VoucherIntegrityProblem> problems = await Service.CheckFilesAsync();
        problems.Should().ContainSingle().Which.Problem.Should().Contain("fehlt");
    }

    [Fact]
    public async Task Ausfuehrbare_Dateitypen_werden_abgewiesen()
    {
        foreach (string type in new[] { "text/html", "image/svg+xml", "application/x-sh", "application/javascript" })
        {
            Func<Task> act = () => Service.AddAsync(Invoice(), Pdf(), "böse.html", type);
            (await act.Should().ThrowAsync<VoucherFileException>())
                .Which.Message.Should().Contain("nicht vorgesehen");
        }
    }

    [Fact]
    public async Task Ein_Dateiname_mit_Pfadanteil_landet_nicht_im_Dateisystem()
    {
        Voucher voucher = await Service.AddAsync(
            Invoice(), Pdf(), "../../../appsettings.json", "application/pdf");

        // Der hochgeladene Name wird nur angezeigt, nie als Pfad verwendet.
        voucher.File!.OriginalName.Should().NotContain("..");
        voucher.File.RelativePath.Should().StartWith("2026/");
        voucher.File.RelativePath.Should().NotContain("..");

        string written = Directory.GetFiles(dataDirectory, "*", SearchOption.AllDirectories).Single();
        Path.GetFullPath(written).Should().StartWith(Path.GetFullPath(dataDirectory));
    }

    [Fact]
    public async Task Eine_leere_Datei_wird_abgewiesen()
    {
        Func<Task> act = () => Service.AddAsync(
            Invoice(), new MemoryStream(), "leer.pdf", "application/pdf");

        (await act.Should().ThrowAsync<VoucherFileException>()).Which.Message.Should().Contain("leer");
    }

    [Fact]
    public async Task Ein_Beleg_ohne_Betrag_wird_abgewiesen()
    {
        Voucher voucher = Invoice(gross: 0m);

        Func<Task> act = () => Service.AddAsync(voucher, Pdf(), "a.pdf", "application/pdf");

        await act.Should().ThrowAsync<VoucherException>();
    }

    [Fact]
    public async Task Das_Belegdatum_muss_im_Geschaeftsjahr_liegen()
    {
        Voucher voucher = Invoice();
        voucher.Date = new DateOnly(2025, 12, 30);

        Func<Task> act = () => Service.AddAsync(voucher, Pdf(), "a.pdf", "application/pdf");

        (await act.Should().ThrowAsync<VoucherException>())
            .Which.Problems.Should().Contain(p => p.Contains("Geschäftsjahr"));
    }

    [Fact]
    public async Task Mit_der_Buchung_wird_auch_der_Beleg_festgeschrieben()
    {
        Voucher voucher = await Service.AddAsync(Invoice(), Pdf(), "a.pdf", "application/pdf");
        JournalEntry entry = await Journal.AddAsync(RentPayment());

        await Service.LinkAsync(voucher.Id, entry.Id);
        await Journal.CommitAsync(entry.Id);

        Context.ChangeTracker.Clear();
        Voucher stored = Context.Vouchers.Single(v => v.Id == voucher.Id);
        stored.CommittedAt.Should().NotBeNull("Beleg und Buchung gehören zusammen");
    }

    [Fact]
    public async Task Ein_festgeschriebener_Beleg_laesst_sich_nicht_mehr_aendern()
    {
        Voucher voucher = await Service.AddAsync(Invoice(), Pdf(), "a.pdf", "application/pdf");
        JournalEntry entry = await Journal.AddAsync(RentPayment());
        await Service.LinkAsync(voucher.Id, entry.Id);
        await Journal.CommitAsync(entry.Id);

        voucher.Description = "nachträglich umbenannt";

        Func<Task> act = () => Context.SaveChangesAsync();
        await act.Should().ThrowAsync<ImmutableRecordException>();
    }

    private static VoucherChanges Corrected(
        decimal gross = 238m,
        string description = "Stromrechnung März",
        string? externalNumber = null,
        int year = 2026,
        int month = 3,
        int day = 15) => new(
            new DateOnly(year, month, day),
            gross,
            description,
            externalNumber,
            null,
            null,
            VoucherDirection.Incoming);

    [Fact]
    public async Task Die_Rechnungsnummer_laesst_sich_nachtragen()
    {
        Voucher voucher = await Service.AddAsync(Invoice(), Pdf(), "a.pdf", "application/pdf");
        voucher.ExternalNumber.Should().BeNull();

        await Service.UpdateAsync(voucher.Id, Corrected(externalNumber: "RE-2026-0412"));

        Context.ChangeTracker.Clear();
        Context.Vouchers.Single(v => v.Id == voucher.Id)
            .ExternalNumber.Should().Be("RE-2026-0412");
    }

    [Fact]
    public async Task Die_Datei_bleibt_beim_Richtigstellen_unberuehrt()
    {
        Voucher voucher = await Service.AddAsync(Invoice(), Pdf(), "a.pdf", "application/pdf");
        string before = voucher.File!.Sha256;

        await Service.UpdateAsync(voucher.Id, Corrected(gross: 999m, description: "ganz anders"));

        Context.ChangeTracker.Clear();
        Voucher stored = Context.Vouchers.Include(v => v.File).Single(v => v.Id == voucher.Id);
        stored.File!.Sha256.Should().Be(before);
        (await Service.CheckFilesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Die_Belegnummer_bleibt_beim_Richtigstellen_stehen()
    {
        Voucher voucher = await Service.AddAsync(Invoice(), Pdf(), "a.pdf", "application/pdf");

        Voucher changed = await Service.UpdateAsync(voucher.Id, Corrected(description: "neu"));

        changed.Label.Should().Be("2026-00001");
        changed.Source.Should().Be(VoucherSource.Upload);
    }

    [Fact]
    public async Task Ein_Beleg_wechselt_das_Geschaeftsjahr_nicht()
    {
        Voucher voucher = await Service.AddAsync(Invoice(), Pdf(), "a.pdf", "application/pdf");

        Func<Task> act = () => Service.UpdateAsync(voucher.Id, Corrected(year: 2025));

        (await act.Should().ThrowAsync<VoucherException>())
            .Which.Problems.Should().Contain(p => p.Contains("Geschaeftsjahr"));
    }

    [Fact]
    public async Task Ein_festgeschriebener_Beleg_nimmt_keine_Richtigstellung_mehr_an()
    {
        Voucher voucher = await Service.AddAsync(Invoice(), Pdf(), "a.pdf", "application/pdf");
        JournalEntry entry = await Journal.AddAsync(RentPayment());
        await Service.LinkAsync(voucher.Id, entry.Id);
        await Journal.CommitAsync(entry.Id);
        Context.ChangeTracker.Clear();

        Func<Task> act = () => Service.UpdateAsync(voucher.Id, Corrected(description: "zu spaet"));

        (await act.Should().ThrowAsync<VoucherException>())
            .Which.Problems.Should().Contain(p => p.Contains("festgeschrieben"));
    }

    [Fact]
    public async Task Eine_Richtigstellung_ins_Unsinnige_wird_abgewiesen()
    {
        Voucher voucher = await Service.AddAsync(Invoice(), Pdf(), "a.pdf", "application/pdf");

        Func<Task> act = () => Service.UpdateAsync(voucher.Id, Corrected(gross: -5m));

        await act.Should().ThrowAsync<VoucherException>();

        // Und zwar ganz: Was nicht durchgeht, darf auch nicht halb ankommen.
        Context.ChangeTracker.Clear();
        Context.Vouchers.Single(v => v.Id == voucher.Id).GrossAmount.Should().Be(238m);
    }

    [Fact]
    public async Task An_eine_festgeschriebene_Buchung_kommt_kein_Beleg_mehr()
    {
        JournalEntry entry = await Journal.AddAsync(RentPayment());
        await Journal.CommitAsync(entry.Id);
        Voucher voucher = await Service.AddAsync(Invoice(), Pdf(), "a.pdf", "application/pdf");

        Func<Task> act = () => Service.LinkAsync(voucher.Id, entry.Id);

        (await act.Should().ThrowAsync<VoucherException>())
            .Which.Problems.Should().Contain(p => p.Contains("festgeschrieben"));
    }
}
