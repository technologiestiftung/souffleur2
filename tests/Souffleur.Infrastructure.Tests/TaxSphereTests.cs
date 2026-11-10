using FluentAssertions;
using Souffleur.Domain.Accounting;
using Souffleur.Domain.Organizations;
using Souffleur.Infrastructure.Accounting;

namespace Souffleur.Infrastructure.Tests;

/// <summary>
/// Gemeinnützige Häuser trennen ihre Einnahmen und Ausgaben nach vier Sphären.
/// Im SKR42 steht die Sphäre nicht in der Kontonummer -- anders als im
/// abgelösten SKR49 --, also muss sie an der Buchung hängen, und souffleur muss
/// sie einfordern.
/// </summary>
public class TaxSphereTests : AccountingTestBase
{
    private async Task MakeNonProfitAsync(ChartOfAccounts chart = ChartOfAccounts.Skr42)
    {
        Context.Organizations.Add(new Organization
        {
            Name = "Theater am Kanal e. V.",
            ChartOfAccounts = chart,
            IsNonProfit = true,
        });
        await Context.SaveChangesAsync();
    }

    [Fact]
    public void Ein_gemeinnuetziges_Haus_verlangt_Sphaeren()
    {
        var verein = new Organization { Name = "Verein", ChartOfAccounts = ChartOfAccounts.Skr42, IsNonProfit = true };
        var gmbh = new Organization { Name = "GmbH", ChartOfAccounts = ChartOfAccounts.Skr03, IsNonProfit = false };

        verein.RequiresTaxSpheres.Should().BeTrue();
        gmbh.RequiresTaxSpheres.Should().BeFalse();
    }

    [Fact]
    public void SKR42_verlangt_Sphaeren_auch_ohne_Gemeinnuetzigkeit()
    {
        ChartOfAccounts.Skr42.UsesTaxSpheres().Should().BeTrue();
        ChartOfAccounts.Skr03.UsesTaxSpheres().Should().BeFalse();
        ChartOfAccounts.Skr04.UsesTaxSpheres().Should().BeFalse();
    }

    [Fact]
    public async Task Ohne_Sphaere_wird_die_Buchung_abgewiesen()
    {
        await MakeNonProfitAsync();

        Func<Task> act = () => Journal.AddAsync(RentPayment());

        (await act.Should().ThrowAsync<JournalException>())
            .Which.Problems.Should().Contain(p => p.Contains("steuerliche Sphäre"));
    }

    [Fact]
    public async Task Mit_Sphaere_geht_die_Buchung_durch()
    {
        await MakeNonProfitAsync();

        JournalEntry entry = RentPayment();
        entry.Lines[0].TaxSphere = TaxSphere.PurposeOperation;

        JournalEntry stored = await Journal.AddAsync(entry);

        stored.Number.Should().Be(1);
        stored.Lines[0].TaxSphere.Should().Be(TaxSphere.PurposeOperation);
    }

    [Fact]
    public async Task Bank_und_Kasse_brauchen_keine_Sphaere()
    {
        await MakeNonProfitAsync();

        JournalEntry entry = RentPayment();
        // Nur das Aufwandskonto bekommt eine; die Bankzeile bleibt ohne.
        entry.Lines[0].TaxSphere = TaxSphere.PurposeOperation;

        Func<Task> act = () => Journal.AddAsync(entry);

        await act.Should().NotThrowAsync("der Geldfluss selbst gehört keiner Tätigkeit an");
    }

    [Fact]
    public async Task Das_Konto_schlaegt_seine_Sphaere_vor()
    {
        await MakeNonProfitAsync();

        // Das Mietkonto gehört beim Theater zum Zweckbetrieb -- einmal am Konto
        // hinterlegt, muss es niemand je Buchung wiederholen.
        Rent.DefaultTaxSphere = TaxSphere.PurposeOperation;
        await Context.SaveChangesAsync();

        JournalEntry stored = await Journal.AddAsync(RentPayment());

        stored.Lines.Single(l => l.AccountId == Rent.Id)
            .TaxSphere.Should().Be(TaxSphere.PurposeOperation);
    }

    [Fact]
    public async Task Die_Buchung_sticht_den_Vorschlag_des_Kontos()
    {
        await MakeNonProfitAsync();
        Rent.DefaultTaxSphere = TaxSphere.PurposeOperation;
        await Context.SaveChangesAsync();

        JournalEntry entry = RentPayment();
        entry.Lines[0].TaxSphere = TaxSphere.CommercialOperation;

        JournalEntry stored = await Journal.AddAsync(entry);

        stored.Lines.Single(l => l.AccountId == Rent.Id)
            .TaxSphere.Should().Be(TaxSphere.CommercialOperation);
    }

    [Fact]
    public async Task Ein_Haus_ohne_Gemeinnuetzigkeit_bucht_ohne_Sphaere()
    {
        Context.Organizations.Add(new Organization
        {
            Name = "Theater am Kanal GmbH",
            ChartOfAccounts = ChartOfAccounts.Skr03,
            IsNonProfit = false,
        });
        await Context.SaveChangesAsync();

        Func<Task> act = () => Journal.AddAsync(RentPayment());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void Jede_Sphaere_traegt_ihren_Rechtsbegriff_und_ein_Beispiel()
    {
        TaxSphere.PurposeOperation.Label().Should().Be("Zweckbetrieb");
        TaxSphere.CommercialOperation.Label().Should().Be("Wirtschaftlicher Geschäftsbetrieb");
        TaxSphere.PurposeOperation.Example().Should().Contain("Eintritt");
        TaxSphere.CommercialOperation.Example().Should().Contain("Foyer");
    }
}
