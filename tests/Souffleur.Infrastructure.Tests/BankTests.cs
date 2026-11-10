using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Accounting;
using Souffleur.Domain.Banking;
using Souffleur.Domain.Contacts;
using Souffleur.Infrastructure.Banking;

namespace Souffleur.Infrastructure.Tests;

/// <summary>
/// Kontoauszüge einlesen und abgleichen. Was hier schiefgeht, merkt man an
/// einer Buchhaltung, die nicht stimmt -- und zwar Monate später.
/// </summary>
public class BankTests : AccountingTestBase
{
    private BankService Service => new(Context, TimeProvider.System);

    private const string Iban = "DE89370400440532013000";

    private async Task<BankAccount> AccountAsync(string iban = Iban) =>
        await Service.AddAccountAsync(new BankAccount { Name = "Geschäftskonto", Iban = iban });

    /// <summary>
    /// Ein Auszug, wie ihn eine Bank liefert. Der Namensraum ist Absicht: Er
    /// wechselt je nach Fassung, und der Leser darf sich nicht daran halten.
    /// </summary>
    private static Stream Statement(
        string entries, string iban = Iban, string balance = "1234.56") =>
        new MemoryStream(Encoding.UTF8.GetBytes($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.08">
              <BkToCstmrStmt>
                <GrpHdr><MsgId>M1</MsgId></GrpHdr>
                <Stmt>
                  <Id>A-2026-03</Id>
                  <Acct><Id><IBAN>{iban}</IBAN></Id></Acct>
                  <Bal>
                    <Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp>
                    <Amt Ccy="EUR">{balance}</Amt>
                    <CdtDbtInd>CRDT</CdtDbtInd>
                    <Dt><Dt>2026-03-31</Dt></Dt>
                  </Bal>
                  {entries}
                </Stmt>
              </BkToCstmrStmt>
            </Document>
            """));

    private static string Entry(
        string amount = "247.80",
        string direction = "DBIT",
        string booked = "2026-03-12",
        string purpose = "Rechnung RE-2026-0412",
        string name = "Bühnenbedarf Schmidt GmbH",
        string counterIban = "DE02120300000000202051",
        string? reference = "REF-1",
        string status = "BOOK") => $"""
            <Ntry>
              <Amt Ccy="EUR">{amount}</Amt>
              <CdtDbtInd>{direction}</CdtDbtInd>
              <Sts><Cd>{status}</Cd></Sts>
              <BookgDt><Dt>{booked}</Dt></BookgDt>
              <ValDt><Dt>{booked}</Dt></ValDt>
              {(reference is null ? "" : $"<AcctSvcrRef>{reference}</AcctSvcrRef>")}
              <NtryDtls><TxDtls>
                <Refs><EndToEndId>E2E-7</EndToEndId></Refs>
                <RltdPties>
                  <Cdtr><Nm>{name}</Nm></Cdtr>
                  <CdtrAcct><Id><IBAN>{counterIban}</IBAN></Id></CdtrAcct>
                  <Dbtr><Nm>{name}</Nm></Dbtr>
                  <DbtrAcct><Id><IBAN>{counterIban}</IBAN></Id></DbtrAcct>
                </RltdPties>
                <RmtInf><Ustrd>{purpose}</Ustrd></RmtInf>
              </TxDtls></NtryDtls>
            </Ntry>
            """;

    // --- Die IBAN ---

    [Theory]
    [InlineData("DE89370400440532013000", true)]
    [InlineData("DE89 3704 0044 0532 0130 00", true)]
    [InlineData("DE89370400440532013001", false)]
    [InlineData("XX89370400440532013000", false)]
    [InlineData("DE89", false)]
    public void Die_Pruefziffer_der_IBAN_wird_geprueft(string iban, bool valid)
    {
        // Ein Zahlendreher fällt sonst erst auf, wenn das Geld woanders ist.
        BankAccount.IsValidIban(iban).Should().Be(valid);
    }

    [Fact]
    public async Task Ein_Konto_mit_falscher_IBAN_wird_abgelehnt()
    {
        Func<Task> create = () => Service.AddAccountAsync(
            new BankAccount { Name = "Falsch", Iban = "DE89370400440532013001" });

        await create.Should().ThrowAsync<BankException>().WithMessage("*Prüfziffer*");
    }

    [Fact]
    public async Task Dasselbe_Konto_gibt_es_nur_einmal()
    {
        await AccountAsync();

        // Mit Leerzeichen geschrieben ist es dieselbe IBAN.
        Func<Task> again = () => Service.AddAccountAsync(
            new BankAccount { Name = "Nochmal", Iban = "DE89 3704 0044 0532 0130 00" });

        await again.Should().ThrowAsync<BankException>().WithMessage("*schon als Konto*");
    }

    // --- Einlesen ---

    [Fact]
    public async Task Ein_Auszug_bringt_seine_Umsaetze_und_den_Saldo_mit()
    {
        BankAccount account = await AccountAsync();

        ImportResult result = await Service.ImportAsync(account.Id, Statement(Entry()));

        result.Added.Should().Be(1);
        result.Balance.Should().Be(1234.56m);
        result.BalanceDate.Should().Be(new DateOnly(2026, 3, 31));

        BankTransaction transaction = await Context.BankTransactions.SingleAsync();

        // Belastung heißt Minus: Betrag und Richtung stehen im Auszug
        // getrennt, hier zusammen.
        transaction.Amount.Should().Be(-247.80m);
        transaction.IsIncoming.Should().BeFalse();
        transaction.Purpose.Should().Be("Rechnung RE-2026-0412");
        transaction.CounterpartyName.Should().Be("Bühnenbedarf Schmidt GmbH");
        transaction.CounterpartyIban.Should().Be("DE02120300000000202051");
        transaction.EndToEndId.Should().Be("E2E-7");
    }

    [Fact]
    public async Task Eine_Gutschrift_steht_mit_Plus_da()
    {
        BankAccount account = await AccountAsync();

        await Service.ImportAsync(account.Id, Statement(Entry(direction: "CRDT")));

        (await Context.BankTransactions.SingleAsync()).Amount.Should().Be(247.80m);
    }

    [Fact]
    public async Task Derselbe_Auszug_zweimal_verdoppelt_nichts()
    {
        BankAccount account = await AccountAsync();

        await Service.ImportAsync(account.Id, Statement(Entry()));
        ImportResult second = await Service.ImportAsync(account.Id, Statement(Entry()));

        // Niemand weiß, welche Datei schon dran war -- also darf es egal sein.
        second.Added.Should().Be(0);
        second.AlreadyKnown.Should().Be(1);
        (await Context.BankTransactions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Auch_ohne_Bankreferenz_wird_nichts_verdoppelt()
    {
        BankAccount account = await AccountAsync();

        await Service.ImportAsync(account.Id, Statement(Entry(reference: null)));
        await Service.ImportAsync(account.Id, Statement(Entry(reference: null)));

        // Dann trägt der Fingerabdruck aus Datum, Betrag, Zweck und Gegenseite.
        (await Context.BankTransactions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Vorgemerktes_wird_nicht_uebernommen()
    {
        BankAccount account = await AccountAsync();

        await Service.ImportAsync(account.Id, Statement(Entry(status: "PDNG")));

        // Vorgemerktes ändert sich noch -- dagegen abzugleichen hieße, gegen
        // etwas abzugleichen, das es nicht gibt.
        (await Context.BankTransactions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Ein_Auszug_fuer_ein_anderes_Konto_wird_abgelehnt()
    {
        BankAccount account = await AccountAsync();

        Func<Task> wrong = () => Service.ImportAsync(
            account.Id, Statement(Entry(), iban: "DE02120300000000202051"));

        await wrong.Should().ThrowAsync<BankException>().WithMessage("*gehört zu*");
    }

    [Fact]
    public async Task Eine_Datei_die_kein_Auszug_ist_wird_benannt()
    {
        BankAccount account = await AccountAsync();
        var nonsense = new MemoryStream(Encoding.UTF8.GetBytes("<html><body>nein</body></html>"));

        Func<Task> read = () => Service.ImportAsync(account.Id, nonsense);

        await read.Should().ThrowAsync<Camt053Exception>().WithMessage("*CAMT.053*");
    }

    // --- Abgleich ---

    [Fact]
    public async Task Ein_Beleg_mit_gleichem_Betrag_wird_vorgeschlagen()
    {
        BankAccount account = await AccountAsync();
        await Service.ImportAsync(account.Id, Statement(Entry()));

        Context.Vouchers.Add(new Voucher
        {
            Number = 1,
            FiscalYear = 2026,
            Date = new DateOnly(2026, 3, 9),
            GrossAmount = 247.80m,
            Description = "Beleuchtungsmittel",
            ExternalNumber = "RE-2026-0412",
            Direction = VoucherDirection.Incoming,
            Source = VoucherSource.Upload,
        });
        await Context.SaveChangesAsync();

        BankTransaction transaction = await Context.BankTransactions.SingleAsync();
        List<MatchSuggestion> found = await Service.SuggestAsync(transaction.Id);

        MatchSuggestion best = found.Should().ContainSingle().Subject;
        best.Description.Should().Be("Beleuchtungsmittel");

        // Die Rechnungsnummer steht im Verwendungszweck -- das wiegt schwerer
        // als der gleiche Betrag allein.
        best.Reason.Should().Contain("Rechnungsnummer");
    }

    [Fact]
    public async Task Ein_Beleg_weit_vor_dem_Umsatz_wird_nicht_vorgeschlagen()
    {
        BankAccount account = await AccountAsync();
        await Service.ImportAsync(account.Id, Statement(Entry()));

        Context.Vouchers.Add(new Voucher
        {
            Number = 2,
            FiscalYear = 2026,
            Date = new DateOnly(2026, 1, 2),
            GrossAmount = 247.80m,
            Description = "Zufällig derselbe Betrag",
            Direction = VoucherDirection.Incoming,
            Source = VoucherSource.Upload,
        });
        await Context.SaveChangesAsync();

        BankTransaction transaction = await Context.BankTransactions.SingleAsync();

        // Zwei Monate auseinander: Gleicher Betrag allein ist kein Beweis.
        (await Service.SuggestAsync(transaction.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Die_IBAN_des_Lieferanten_wiegt_schwer()
    {
        BankAccount account = await AccountAsync();
        await Service.ImportAsync(account.Id, Statement(Entry(purpose: "Sammelzahlung")));

        var supplier = new Contact
        {
            Kind = ContactKind.Organization,
            Name = "Bühnenbedarf Schmidt GmbH",
            Roles = [new ContactRoleAssignment { Role = ContactRole.Supplier }],
            BankDetails = new ContactBankDetails { Iban = "DE02 1203 0000 0000 2020 51" },
        };
        Context.Contacts.Add(supplier);
        await Context.SaveChangesAsync();

        Context.Vouchers.Add(new Voucher
        {
            Number = 3,
            FiscalYear = 2026,
            Date = new DateOnly(2026, 3, 10),
            GrossAmount = 247.80m,
            Description = "Lieferung",
            ContactId = supplier.Id,
            Direction = VoucherDirection.Incoming,
            Source = VoucherSource.Upload,
        });
        await Context.SaveChangesAsync();

        BankTransaction transaction = await Context.BankTransactions.SingleAsync();
        MatchSuggestion best = (await Service.SuggestAsync(transaction.Id)).First();

        best.Reason.Should().Contain("IBAN");
    }

    [Fact]
    public async Task Ein_Umsatz_laesst_sich_einem_Beleg_zuordnen()
    {
        BankAccount account = await AccountAsync();
        await Service.ImportAsync(account.Id, Statement(Entry()));

        var voucher = new Voucher
        {
            Number = 4,
            FiscalYear = 2026,
            Date = new DateOnly(2026, 3, 9),
            GrossAmount = 247.80m,
            Description = "Beleuchtungsmittel",
            Direction = VoucherDirection.Incoming,
            Source = VoucherSource.Upload,
        };
        Context.Vouchers.Add(voucher);
        await Context.SaveChangesAsync();

        BankTransaction transaction = await Context.BankTransactions.SingleAsync();
        await Service.AssignAsync(transaction.Id, voucher.Id, null);

        BankTransaction after = await Context.BankTransactions.AsNoTracking()
            .SingleAsync(t => t.Id == transaction.Id);

        after.VoucherId.Should().Be(voucher.Id);
        after.IsAssigned.Should().BeTrue();

        // Und er taucht nicht mehr unter den offenen auf.
        (await Service.ListTransactionsAsync(unassignedOnly: true)).Should().BeEmpty();
    }

    [Fact]
    public async Task Ein_Konto_mit_Umsaetzen_laesst_sich_nicht_loeschen()
    {
        BankAccount account = await AccountAsync();
        await Service.ImportAsync(account.Id, Statement(Entry()));

        Func<Task> delete = () => Service.DeleteAccountAsync(account.Id);

        await delete.Should().ThrowAsync<BankException>().WithMessage("*Umsätze*");
    }
}
