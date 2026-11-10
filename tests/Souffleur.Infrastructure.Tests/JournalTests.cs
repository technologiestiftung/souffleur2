using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Accounting;
using Souffleur.Infrastructure.Accounting;

namespace Souffleur.Infrastructure.Tests;

/// <summary>
/// Die Regeln der doppelten Buchführung. Was hier durchgeht, landet später im
/// DATEV-Stapel beim Steuerbüro -- deshalb ist jede Regel einzeln festgehalten.
/// </summary>
public class JournalTests : AccountingTestBase
{
    [Fact]
    public async Task Eine_ausgeglichene_Buchung_geht_durch()
    {
        JournalEntry entry = await Journal.AddAsync(RentPayment());

        entry.Number.Should().Be(1);
        entry.DebitTotal.Should().Be(entry.CreditTotal);
        entry.IsCommitted.Should().BeFalse("eine neue Buchung ist zunächst ein Entwurf");
    }

    [Fact]
    public async Task Soll_und_Haben_muessen_uebereinstimmen()
    {
        JournalEntry entry = RentPayment();
        entry.Lines[1].Credit = 1400m;   // 100 Euro zu wenig

        Func<Task> act = () => Journal.AddAsync(entry);

        (await act.Should().ThrowAsync<JournalException>())
            .Which.Problems.Should().ContainSingle()
            .Which.Should().Contain("Soll und Haben");
    }

    [Fact]
    public async Task Eine_Buchung_braucht_zwei_Zeilen()
    {
        JournalEntry entry = RentPayment();
        entry.Lines.RemoveAt(1);

        Func<Task> act = () => Journal.AddAsync(entry);

        (await act.Should().ThrowAsync<JournalException>())
            .Which.Problems.Should().Contain(p => p.Contains("mindestens zwei Zeilen"));
    }

    [Fact]
    public async Task Eine_Zeile_steht_nicht_im_Soll_und_im_Haben()
    {
        JournalEntry entry = RentPayment();
        entry.Lines[0].Credit = 50m;

        Func<Task> act = () => Journal.AddAsync(entry);

        await act.Should().ThrowAsync<JournalException>();
    }

    [Fact]
    public async Task Eine_Buchung_ueber_null_wird_abgelehnt()
    {
        JournalEntry entry = RentPayment(amount: 0m);

        Func<Task> act = () => Journal.AddAsync(entry);

        await act.Should().ThrowAsync<JournalException>();
    }

    [Fact]
    public async Task Das_Datum_muss_im_Geschaeftsjahr_liegen()
    {
        JournalEntry entry = RentPayment();
        entry.Date = new DateOnly(2025, 12, 31);

        Func<Task> act = () => Journal.AddAsync(entry);

        (await act.Should().ThrowAsync<JournalException>())
            .Which.Problems.Should().Contain(p => p.Contains("Geschäftsjahr"));
    }

    [Fact]
    public async Task Die_Journalnummer_laeuft_lueckenlos_weiter()
    {
        JournalEntry first = await Journal.AddAsync(RentPayment());
        JournalEntry second = await Journal.AddAsync(RentPayment());
        JournalEntry third = await Journal.AddAsync(RentPayment());

        new[] { first.Number, second.Number, third.Number }.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Jedes_Geschaeftsjahr_faengt_wieder_bei_eins_an()
    {
        await Journal.AddAsync(RentPayment(year: 2026));
        await Journal.AddAsync(RentPayment(year: 2026));

        JournalEntry newYear = await Journal.AddAsync(RentPayment(year: 2027));

        newYear.Number.Should().Be(1);
    }

    [Fact]
    public async Task Centbetraege_bleiben_exakt()
    {
        // 0.1 + 0.2 ist in Gleitkomma nicht 0.3 -- in Cent schon.
        JournalEntry entry = RentPayment(amount: 1234.56m);
        await Journal.AddAsync(entry);

        Context.ChangeTracker.Clear();
        JournalEntry stored = await Context.JournalEntries
            .Include(e => e.Lines)
            .SingleAsync(e => e.Id == entry.Id);

        stored.DebitTotal.Should().Be(1234.56m);
    }
}
