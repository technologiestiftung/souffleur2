using FluentAssertions;
using Souffleur.Domain.Accounting;
using Souffleur.Domain.Contacts;
using Souffleur.Domain.Personnel;
using Souffleur.Infrastructure.Personnel;

namespace Souffleur.Infrastructure.Tests;

/// <summary>
/// Die Kuenstlersozialabgabe: was zusammengezaehlt wird, was nicht, und was
/// das Haus selbst entscheiden muss.
/// </summary>
public class ArtistsSocialFundTests : AccountingTestBase
{
    private ArtistsSocialFundService Service => new(Context);

    private static int Year => 2026;

    private async Task<Contact> ArtistAsync(string surname, bool subject, bool artistRole = true)
    {
        var contact = new Contact
        {
            Kind = ContactKind.Person,
            Name = $"{surname}, Mara",
            GivenName = "Mara",
            Surname = surname,
            Roles = artistRole
                ? [new ContactRoleAssignment { Role = ContactRole.Artist }]
                : [new ContactRoleAssignment { Role = ContactRole.Supplier }],
            BankDetails = new ContactBankDetails { IsSubjectToArtistsSocialFund = subject },
        };

        Context.Contacts.Add(contact);
        await Context.SaveChangesAsync();
        return contact;
    }

    private async Task<Voucher> VoucherAsync(
        Contact contact,
        decimal gross,
        VoucherDirection direction = VoucherDirection.Incoming,
        JournalEntry? booked = null)
    {
        var voucher = new Voucher
        {
            FiscalYear = Year,
            Number = Random.Shared.Next(1, 90_000),
            Date = new DateOnly(Year, 5, 4),
            GrossAmount = gross,
            Description = "Gage",
            Direction = direction,
            Source = VoucherSource.PaperOnly,
            ContactId = contact.Id,
            JournalEntryId = booked?.Id,
        };

        Context.Vouchers.Add(voucher);
        await Context.SaveChangesAsync();
        return voucher;
    }

    [Fact]
    public async Task Ohne_hinterlegten_Satz_gibt_es_eine_Grundlage_aber_keine_Abgabe()
    {
        Contact artist = await ArtistAsync("Lenz", subject: true);
        await VoucherAsync(artist, 1000m);

        ArtistsSocialFundReport report = await Service.ReportAsync(Year);

        report.Base.Should().Be(1000m);
        report.Rate.Should().BeNull();
        report.Levy.Should().BeNull();
    }

    [Fact]
    public async Task Mit_Satz_wird_die_Abgabe_gerechnet()
    {
        Contact artist = await ArtistAsync("Lenz", subject: true);
        await VoucherAsync(artist, 1000m);
        await Service.SetRateAsync(Year, 5.0m);

        ArtistsSocialFundReport report = await Service.ReportAsync(Year);

        report.Levy.Should().Be(50.00m);
    }

    [Fact]
    public async Task Was_das_Haus_einnimmt_zaehlt_nicht()
    {
        Contact artist = await ArtistAsync("Lenz", subject: true);
        await VoucherAsync(artist, 1000m, VoucherDirection.Outgoing);

        (await Service.ReportAsync(Year)).Base.Should().Be(0m);
    }

    [Fact]
    public async Task Ein_anderes_Jahr_zaehlt_nicht()
    {
        Contact artist = await ArtistAsync("Lenz", subject: true);
        await VoucherAsync(artist, 1000m);

        (await Service.ReportAsync(Year - 1)).Base.Should().Be(0m);
    }

    [Fact]
    public async Task Bei_einer_gebuchten_Rechnung_zaehlt_das_Entgelt_ohne_Steuer()
    {
        Contact artist = await ArtistAsync("Lenz", subject: true);

        JournalEntry entry = await Journal.AddAsync(new JournalEntry
        {
            FiscalYear = Year,
            Date = new DateOnly(Year, 5, 4),
            Description = "Gage Mai",
            Lines =
            [
                new JournalLine { AccountId = Rent.Id, Debit = 1000m, TaxAmount = 190m },
                new JournalLine { AccountId = Bank.Id, Credit = 1000m },
            ],
        });

        await VoucherAsync(artist, 1000m, booked: entry);

        ArtistsSocialFundReport report = await Service.ReportAsync(Year);

        report.Base.Should().Be(810m);
        report.UnbookedCount.Should().Be(0);
    }

    [Fact]
    public async Task Ungebuchte_Belege_stehen_brutto_und_werden_gezaehlt()
    {
        Contact artist = await ArtistAsync("Lenz", subject: true);
        await VoucherAsync(artist, 1190m);

        ArtistsSocialFundReport report = await Service.ReportAsync(Year);

        report.Base.Should().Be(1190m);
        report.UnbookedCount.Should().Be(1);
    }

    [Fact]
    public async Task Wer_kuenstlerisch_arbeitet_aber_nicht_gekennzeichnet_ist_faellt_auf()
    {
        Contact artist = await ArtistAsync("Brandt", subject: false);
        await VoucherAsync(artist, 500m);

        ArtistsSocialFundReport report = await Service.ReportAsync(Year);

        report.Base.Should().Be(0m);
        report.Unflagged.Should().ContainSingle()
            .Which.Name.Should().Be("Brandt, Mara");
    }

    [Fact]
    public async Task Ein_Lieferant_ohne_kuenstlerische_Rolle_faellt_nicht_auf()
    {
        Contact supplier = await ArtistAsync("Druckerei", subject: false, artistRole: false);
        await VoucherAsync(supplier, 500m);

        (await Service.ReportAsync(Year)).Unflagged.Should().BeEmpty();
    }

    [Fact]
    public async Task Mehrere_Belege_derselben_Person_werden_zusammengezaehlt()
    {
        Contact artist = await ArtistAsync("Lenz", subject: true);
        await VoucherAsync(artist, 400m);
        await VoucherAsync(artist, 600m);

        ArtistsSocialFundReport report = await Service.ReportAsync(Year);

        report.Flagged.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Amount = 1000m, VoucherCount = 2 });
    }

    [Fact]
    public async Task Ein_Satz_laesst_sich_aendern_und_bleibt_einmalig_je_Jahr()
    {
        await Service.SetRateAsync(Year, 5.0m);
        await Service.SetRateAsync(Year, 4.8m);

        (await Service.RatesAsync()).Should().ContainSingle()
            .Which.Percent.Should().Be(4.8m);
    }

    [Fact]
    public async Task Ein_unmoeglicher_Satz_wird_abgewiesen()
    {
        Func<Task> tooMuch = () => Service.SetRateAsync(Year, 120m);

        await tooMuch.Should().ThrowAsync<ArtistsSocialFundException>()
            .WithMessage("*zwischen 0 und 100*");
    }
}
