using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Accounting;
using Souffleur.Infrastructure.Accounting;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Tests;

/// <summary>
/// Festschreiben und Stornieren. Das ist die GoBD-Mechanik (§ 146 Abs. 4 AO):
/// Eine festgeschriebene Buchung wird nie geändert und nie gelöscht, sondern
/// durch eine Gegenbuchung aufgehoben.
/// </summary>
public class CommitAndReversalTests : AccountingTestBase
{
    [Fact]
    public async Task Ein_Entwurf_laesst_sich_noch_aendern()
    {
        JournalEntry entry = await Journal.AddAsync(RentPayment());

        entry.Description = "Miete März, korrigiert";

        Func<Task> act = () => Context.SaveChangesAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Nach_dem_Festschreiben_ist_die_Buchung_dicht()
    {
        JournalEntry entry = await Journal.AddAsync(RentPayment());
        await Journal.CommitAsync(entry.Id);

        entry.Description = "nachträglich geändert";

        Func<Task> act = () => Context.SaveChangesAsync();
        await act.Should().ThrowAsync<ImmutableRecordException>();
    }

    [Fact]
    public async Task Eine_festgeschriebene_Buchung_laesst_sich_nicht_loeschen()
    {
        JournalEntry entry = await Journal.AddAsync(RentPayment());
        await Journal.CommitAsync(entry.Id);

        Context.JournalEntries.Remove(entry);

        Func<Task> act = () => Context.SaveChangesAsync();
        await act.Should().ThrowAsync<ImmutableRecordException>();
    }

    [Fact]
    public async Task Zweimal_festschreiben_geht_nicht()
    {
        JournalEntry entry = await Journal.AddAsync(RentPayment());
        await Journal.CommitAsync(entry.Id);

        Func<Task> act = () => Journal.CommitAsync(entry.Id);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("bereits");
    }

    [Fact]
    public async Task Eine_unausgeglichene_Buchung_laesst_sich_nicht_festschreiben()
    {
        JournalEntry entry = await Journal.AddAsync(RentPayment());

        // Am Entwurf darf man noch drehen -- festschreiben darf man ihn dann nicht.
        entry.Lines[0].Debit = 999m;
        await Context.SaveChangesAsync();

        Func<Task> act = () => Journal.CommitAsync(entry.Id);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Das_Storno_dreht_die_Seiten_um()
    {
        JournalEntry original = await Journal.AddAsync(RentPayment(1500m));
        await Journal.CommitAsync(original.Id);

        JournalEntry reversal = await Journal.ReverseAsync(
            original.Id, new DateOnly(2026, 4, 2), "Rechnung doppelt erfasst");

        reversal.ReversalOfId.Should().Be(original.Id);
        reversal.Description.Should().Contain("Storno zu Buchung 1");
        reversal.Description.Should().Contain("doppelt erfasst");

        // Die Zeile, die im Original im Soll stand, steht jetzt im Haben.
        JournalLine reversedRent = reversal.Lines.Single(l => l.AccountId == Rent.Id);
        reversedRent.Credit.Should().Be(1500m);
        reversedRent.Debit.Should().Be(0m);
    }

    [Fact]
    public async Task Nach_dem_Storno_gleichen_sich_beide_Buchungen_aus()
    {
        JournalEntry original = await Journal.AddAsync(RentPayment(1500m));
        await Journal.CommitAsync(original.Id);
        await Journal.ReverseAsync(original.Id, new DateOnly(2026, 4, 2), "falscher Betrag");

        Context.ChangeTracker.Clear();
        decimal balance = await Context.JournalLines
            .Where(l => l.AccountId == Rent.Id)
            .SumAsync(l => l.Debit - l.Credit);

        balance.Should().Be(0m, "Buchung und Storno heben sich auf");
    }

    [Fact]
    public async Task Das_Original_bleibt_nach_dem_Storno_im_Journal_stehen()
    {
        JournalEntry original = await Journal.AddAsync(RentPayment());
        await Journal.CommitAsync(original.Id);
        await Journal.ReverseAsync(original.Id, new DateOnly(2026, 4, 2), "Grund");

        Context.ChangeTracker.Clear();
        JournalEntry? stillThere = await Context.JournalEntries.FindAsync(original.Id);

        stillThere.Should().NotBeNull();
        stillThere.Number.Should().Be(1);
    }

    [Fact]
    public async Task Zweimal_stornieren_geht_nicht()
    {
        JournalEntry original = await Journal.AddAsync(RentPayment());
        await Journal.CommitAsync(original.Id);
        await Journal.ReverseAsync(original.Id, new DateOnly(2026, 4, 2), "Grund");

        Func<Task> act = () => Journal.ReverseAsync(original.Id, new DateOnly(2026, 4, 3), "nochmal");

        (await act.Should().ThrowAsync<JournalException>())
            .Which.Problems.Should().Contain(p => p.Contains("bereits storniert"));
    }

    [Fact]
    public async Task Ein_Storno_laesst_sich_nicht_stornieren()
    {
        JournalEntry original = await Journal.AddAsync(RentPayment());
        await Journal.CommitAsync(original.Id);
        JournalEntry reversal = await Journal.ReverseAsync(original.Id, new DateOnly(2026, 4, 2), "Grund");

        Func<Task> act = () => Journal.ReverseAsync(reversal.Id, new DateOnly(2026, 4, 3), "Grund");

        await act.Should().ThrowAsync<JournalException>();
    }

    [Fact]
    public async Task Ein_Storno_ohne_Grund_wird_abgelehnt()
    {
        JournalEntry original = await Journal.AddAsync(RentPayment());
        await Journal.CommitAsync(original.Id);

        Func<Task> act = () => Journal.ReverseAsync(original.Id, new DateOnly(2026, 4, 2), "   ");

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
