using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Souffleur.Domain.Accounting;
using Souffleur.Domain.Organizations;
using Souffleur.Infrastructure.Accounting.Datev;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Tests;

/// <summary>
/// Der Buchungsstapel geht ans Steuerbüro und wird dort maschinell eingelesen.
/// Was hier falsch ist, fällt erst dort auf -- deshalb prüfen diese Tests die
/// erzeugte Datei Feld für Feld.
/// </summary>
public class DatevExportTests : AccountingTestBase
{
    private static readonly DatevOptions Options = new()
    {
        ConsultantNumber = 1234567,
        ClientNumber = 42,
        AccountLength = 4,
        Initials = "WR",
    };

    private DatevExportService Service => new(
        Context, Microsoft.Extensions.Options.Options.Create(Options), TimeProvider.System, new NoUser());

    private async Task<Organization> SetUpHouseAsync(
        ChartOfAccounts chart = ChartOfAccounts.Skr42, bool nonProfit = true)
    {
        var organization = new Organization
        {
            Name = "Theater am Kanal e. V.",
            ChartOfAccounts = chart,
            IsNonProfit = nonProfit,
        };
        Context.Organizations.Add(organization);
        await Context.SaveChangesAsync();
        return organization;
    }

    private static string[] Lines(byte[] content) =>
        DatevBuchungsstapel.Windows1252.GetString(content)
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    private async Task<byte[]> ExportAsync()
    {
        DatevResult result = await Service.ExportAsync(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));
        return result.Content;
    }

    [Fact]
    public async Task Der_Kopf_traegt_Berater_Mandant_und_Zeitraum()
    {
        await SetUpHouseAsync();
        await Journal.AddAsync(WithSphere(RentPayment()));

        string header = Lines(await ExportAsync())[0];
        string[] fields = header.Split(';');

        fields[0].Should().Be("\"EXTF\"");
        fields[1].Should().Be("700");
        fields[2].Should().Be("21");
        fields[3].Should().Be("\"Buchungsstapel\"");
        fields[4].Should().Be("12");
        fields[10].Should().Be("1234567", "Beraternummer an Position 11");
        fields[11].Should().Be("42", "Mandantennummer an Position 12");
        fields[12].Should().Be("20260101", "Beginn des Wirtschaftsjahres");
        fields[13].Should().Be("4", "Sachkontenlänge");
        fields[14].Should().Be("20260301");
        fields[15].Should().Be("20260331");
        fields[20].Should().Be("1", "Festschreibung");
        fields[21].Should().Be("\"EUR\"");
    }

    [Fact]
    public async Task Der_Kopf_nennt_den_Kontenrahmen_des_Hauses()
    {
        await SetUpHouseAsync(ChartOfAccounts.Skr42);
        await Journal.AddAsync(WithSphere(RentPayment()));

        string[] fields = Lines(await ExportAsync())[0].Split(';');

        fields[26].Should().Be("\"42\"", "Sachkontenrahmen an Position 27");
    }

    [Fact]
    public async Task Umlaute_kommen_als_ANSI_nicht_als_UTF8()
    {
        await SetUpHouseAsync();
        JournalEntry entry = WithSphere(RentPayment());
        entry.Description = "Bühnenbild für März";
        await Journal.AddAsync(entry);

        byte[] content = await ExportAsync();

        // In Windows-1252 ist "ü" genau ein Byte (0xFC); in UTF-8 wären es zwei.
        content.Should().Contain((byte)0xFC);
        DatevBuchungsstapel.Windows1252.GetString(content).Should().Contain("Bühnenbild");

        // Gegenprobe: Als UTF-8 gelesen wäre es kaputt.
        Encoding.UTF8.GetString(content).Should().NotContain("Bühnenbild");
    }

    [Fact]
    public async Task Die_Buchungszeile_steht_an_den_richtigen_Stellen()
    {
        await SetUpHouseAsync();
        JournalEntry entry = WithSphere(RentPayment(1500m));
        entry.DocumentNumber = "RE-2026-0042";
        await Journal.AddAsync(entry);

        string[] lines = Lines(await ExportAsync());
        lines.Should().HaveCount(3, "Kopf, Spaltennamen, eine Buchung");

        string[] fields = lines[2].Split(';');
        fields.Should().HaveCount(116);

        fields[0].Should().Be("1500,00", "Umsatz mit Komma, ohne Vorzeichen");
        fields[1].Should().Be("\"S\"", "Soll, weil die erste Zeile im Soll steht");
        fields[2].Should().Be("\"EUR\"");
        fields[6].Should().Be("4210", "Konto");
        fields[7].Should().Be("1200", "Gegenkonto");
        fields[9].Should().Be("0103", "Belegdatum als TTMM");
        fields[10].Should().Be("\"RE-2026-0042\"");
        fields[113].Should().Be("1", "Festschreibung an Position 114");
    }

    [Fact]
    public async Task Die_steuerliche_Sphaere_geht_als_Beleginfo_mit()
    {
        await SetUpHouseAsync();
        await Journal.AddAsync(WithSphere(RentPayment(), TaxSphere.PurposeOperation));

        string[] fields = Lines(await ExportAsync())[2].Split(';');

        fields[20].Should().Be("\"Sphäre\"", "Beleginfo-Art an Position 21");
        fields[21].Should().Be("\"Zweckbetrieb\"", "Beleginfo-Inhalt an Position 22");
    }

    [Fact]
    public async Task Jeder_Betrag_steht_nur_einmal_im_Stapel()
    {
        await SetUpHouseAsync();
        await Journal.AddAsync(WithSphere(RentPayment(1500m)));

        string[] lines = Lines(await ExportAsync());

        // Zwei Buchungszeilen, aber ein Satz -- sonst stünde der Betrag doppelt
        // und das Steuerbüro bekäme die doppelte Summe.
        lines.Skip(2).Should().ContainSingle();
    }

    [Fact]
    public async Task Der_Export_schreibt_die_Buchungen_fest()
    {
        await SetUpHouseAsync();
        JournalEntry entry = await Journal.AddAsync(WithSphere(RentPayment()));
        entry.IsCommitted.Should().BeFalse();

        await ExportAsync();

        Context.ChangeTracker.Clear();
        JournalEntry stored = Context.JournalEntries.Single(e => e.Id == entry.Id);
        stored.IsCommitted.Should().BeTrue("mit der Übergabe ist der Zeitraum geschlossen");
    }

    [Fact]
    public async Task Der_Export_wird_protokolliert()
    {
        await SetUpHouseAsync();
        await Journal.AddAsync(WithSphere(RentPayment(1500m)));

        DatevResult result = await Service.ExportAsync(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

        result.Record.EntryCount.Should().Be(1);
        result.Record.DebitTotal.Should().Be(1500m);
        result.Record.Sha256.Should().HaveLength(64);
        result.Record.FileName.Should().Be("EXTF_Buchungsstapel_20260301_20260331.csv");

        (await Service.HistoryAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Derselbe_Zeitraum_geht_nicht_zweimal()
    {
        await SetUpHouseAsync();
        await Journal.AddAsync(WithSphere(RentPayment()));
        await ExportAsync();

        Func<Task> act = () => Service.ExportAsync(new DateOnly(2026, 3, 15), new DateOnly(2026, 4, 15));

        (await act.Should().ThrowAsync<DatevException>())
            .Which.Problems.Should().Contain(p => p.Contains("überschneidet"));
    }

    [Fact]
    public async Task Ohne_Beraternummer_gibt_es_keinen_Stapel()
    {
        await SetUpHouseAsync();
        await Journal.AddAsync(WithSphere(RentPayment()));

        var incomplete = new DatevExportService(
            Context,
            Microsoft.Extensions.Options.Options.Create(new DatevOptions { ClientNumber = 42 }),
            TimeProvider.System,
            new NoUser());

        Func<Task> act = () => incomplete.ExportAsync(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

        (await act.Should().ThrowAsync<DatevException>())
            .Which.Problems.Should().Contain(p => p.Contains("Beraternummer"));
    }

    [Fact]
    public async Task Ein_leerer_Zeitraum_wird_abgelehnt()
    {
        await SetUpHouseAsync();

        Func<Task> act = () => Service.ExportAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        (await act.Should().ThrowAsync<DatevException>())
            .Which.Problems.Should().Contain(p => p.Contains("keine Buchungen"));
    }

    [Fact]
    public async Task Ein_Zeitraum_ueber_den_Jahreswechsel_wird_abgelehnt()
    {
        await SetUpHouseAsync();
        await Journal.AddAsync(WithSphere(RentPayment()));

        Func<Task> act = () => Service.ExportAsync(new DateOnly(2026, 12, 1), new DateOnly(2027, 1, 31));

        (await act.Should().ThrowAsync<DatevException>())
            .Which.Problems.Should().Contain(p => p.Contains("Jahreswechsel"));
    }

    [Fact]
    public async Task Die_Vorschau_veraendert_nichts()
    {
        await SetUpHouseAsync();
        JournalEntry entry = await Journal.AddAsync(WithSphere(RentPayment(1500m)));

        DatevPreview preview = await Service.PreviewAsync(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

        preview.EntryCount.Should().Be(1);
        preview.DebitTotal.Should().Be(1500m);
        preview.CanExport.Should().BeTrue();

        Context.ChangeTracker.Clear();
        Context.JournalEntries.Single(e => e.Id == entry.Id).IsCommitted
            .Should().BeFalse("eine Vorschau schreibt nichts fest");
    }

    private static JournalEntry WithSphere(JournalEntry entry, TaxSphere sphere = TaxSphere.PurposeOperation)
    {
        entry.Lines[0].TaxSphere = sphere;
        return entry;
    }

    private sealed class NoUser : ICurrentUser
    {
        public Guid? Id => null;
    }
}
