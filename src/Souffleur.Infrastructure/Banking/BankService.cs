using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Accounting;
using Souffleur.Domain.Banking;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Banking;

public class BankException(IReadOnlyList<string> problems)
    : Exception(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;

    public BankException(string problem) : this([problem])
    {
    }
}

/// <summary>Was beim Einlesen eines Auszugs herauskam.</summary>
public sealed record ImportResult(
    int Added, int AlreadyKnown, decimal? Balance, DateOnly? BalanceDate);

/// <summary>Ein Beleg, der zu einem Umsatz passen könnte.</summary>
/// <param name="Score">
/// Wie gut. Nur zum Sortieren gedacht -- entschieden wird von Hand.
/// </param>
public sealed record MatchSuggestion(
    Guid VoucherId, string Label, string Description, decimal GrossAmount,
    DateOnly Date, int Score, string Reason);

/// <summary>
/// Konten, Auszüge und der Abgleich.
/// </summary>
/// <remarks>
/// Was hier **nicht** steht, ist die Verbindung zur Bank selbst. FinTS braucht
/// den ARPA-Stand von libfintx, ein echtes Konto und ein TAN-Gerät -- siehe
/// <c>docs/bankanbindung-uebernahme-aus-arpa.md</c>. Der Auszug kommt vorerst
/// als Datei, und alles darunter funktioniert davon unabhängig.
/// </remarks>
public class BankService(SouffleurDbContext context, TimeProvider time)
{
    /// <summary>
    /// Wie weit ein Beleg zeitlich vom Umsatz entfernt sein darf, um noch
    /// vorgeschlagen zu werden.
    /// </summary>
    /// <remarks>
    /// Zwischen Rechnungsdatum und Abbuchung liegen Zahlungsziele. Sechzig
    /// Tage decken „zahlbar in 30 Tagen" samt Verzug ab; wer weiter sucht,
    /// bekommt Vorschläge, die nur zufällig denselben Betrag haben.
    /// </remarks>
    public const int MatchWindowDays = 60;

    // --- Konten ---

    public async Task<List<BankAccount>> ListAccountsAsync(
        bool includeInactive = false, CancellationToken token = default) =>
        await context.BankAccounts
            .Where(a => includeInactive || a.IsActive)
            .Include(a => a.Account)
            .OrderBy(a => a.Name)
            .AsNoTracking()
            .ToListAsync(token);

    public async Task<BankAccount> AddAccountAsync(
        BankAccount account, CancellationToken token = default)
    {
        account.Iban = BankAccount.Normalize(account.Iban);
        await ValidateAsync(account, token);

        context.BankAccounts.Add(account);
        await context.SaveChangesAsync(token);
        return account;
    }

    public async Task<BankAccount> UpdateAccountAsync(
        Guid id, Action<BankAccount> apply, CancellationToken token = default)
    {
        BankAccount found = await TrackedAsync(id, token);
        apply(found);
        found.Iban = BankAccount.Normalize(found.Iban);
        await ValidateAsync(found, token);
        await context.SaveChangesAsync(token);
        return found;
    }

    public async Task DeleteAccountAsync(Guid id, CancellationToken token = default)
    {
        BankAccount found = await TrackedAsync(id, token);

        int movements = await context.BankTransactions.CountAsync(t => t.BankAccountId == id, token);
        if (movements > 0)
        {
            throw new BankException(
                $"Auf „{found.Name}“ liegen {movements} Umsätze. Abschalten nimmt das Konto " +
                "aus der Auswahl und lässt die Umsätze lesbar.");
        }

        context.BankAccounts.Remove(found);
        await context.SaveChangesAsync(token);
    }

    // --- Auszüge ---

    /// <summary>
    /// Einen Auszug einlesen. Schon bekannte Umsätze werden übergangen.
    /// </summary>
    /// <remarks>
    /// Derselbe Auszug zweimal eingelesen darf nichts verdoppeln -- das
    /// passiert in der Praxis dauernd, weil niemand weiß, welche Datei schon
    /// dran war. Erkannt wird über die Bankreferenz, und wo die fehlt, über
    /// den Fingerabdruck aus Datum, Betrag, Zweck und Gegenseite.
    /// </remarks>
    public async Task<ImportResult> ImportAsync(
        Guid bankAccountId, Stream content, CancellationToken token = default)
    {
        BankAccount account = await TrackedAsync(bankAccountId, token);
        BankStatement statement = Camt053Reader.Read(content);

        if (statement.Iban is { } iban
            && BankAccount.Normalize(iban) != BankAccount.Normalize(account.Iban))
        {
            throw new BankException(
                $"Der Auszug gehört zu {BankAccount.Grouped(iban)}, " +
                $"das Konto zu {account.IbanGrouped}.");
        }

        HashSet<string> knownReferences =
        [
            .. await context.BankTransactions
                .Where(t => t.BankAccountId == bankAccountId && t.BankReference != null)
                .Select(t => t.BankReference!)
                .ToListAsync(token),
        ];

        HashSet<string> knownPrints =
        [
            .. await context.BankTransactions
                .Where(t => t.BankAccountId == bankAccountId)
                .Select(t => t.Fingerprint)
                .ToListAsync(token),
        ];

        int added = 0;
        int known = 0;

        foreach (BankTransaction transaction in statement.Transactions)
        {
            bool seen = transaction.BankReference is { } reference
                ? knownReferences.Contains(reference)
                : knownPrints.Contains(transaction.Fingerprint);

            if (seen)
            {
                known++;
                continue;
            }

            transaction.BankAccountId = bankAccountId;
            transaction.ImportedAt = time.GetUtcNow();
            context.BankTransactions.Add(transaction);

            if (transaction.BankReference is { } added0)
            {
                knownReferences.Add(added0);
            }

            knownPrints.Add(transaction.Fingerprint);
            added++;
        }

        // Der Saldo kommt von der Bank, nicht aus einer Summe: Ein Auszug, der
        // nicht beim ersten Umsatz beginnt, ließe sich sonst nicht aufaddieren.
        if (statement.ClosingBalance is { } balance
            && (account.BalanceDate is null || statement.BalanceDate >= account.BalanceDate))
        {
            account.Balance = balance;
            account.BalanceDate = statement.BalanceDate;
        }

        await context.SaveChangesAsync(token);
        return new ImportResult(added, known, account.Balance, account.BalanceDate);
    }

    public async Task<List<BankTransaction>> ListTransactionsAsync(
        Guid? bankAccountId = null,
        bool unassignedOnly = false,
        int take = 200,
        CancellationToken token = default)
    {
        IQueryable<BankTransaction> query = context.BankTransactions
            .Include(t => t.Voucher)
            .AsNoTracking();

        if (bankAccountId is { } id)
        {
            query = query.Where(t => t.BankAccountId == id);
        }

        if (unassignedOnly)
        {
            query = query.Where(t => t.VoucherId == null && t.JournalEntryId == null);
        }

        return await query
            .OrderByDescending(t => t.BookedOn)
            .ThenByDescending(t => t.ImportedAt)
            .Take(Math.Clamp(take, 1, 1000))
            .ToListAsync(token);
    }

    // --- Abgleich ---

    /// <summary>
    /// Welche offenen Belege zu diesem Umsatz passen könnten.
    /// </summary>
    /// <remarks>
    /// Vorgeschlagen, nicht entschieden. Ein gleicher Betrag ist ein starkes
    /// Zeichen, aber kein Beweis -- zwei Rechnungen über 89 Euro im selben
    /// Monat sind nichts Besonderes. Deshalb zählt die Nähe im Datum mit, und
    /// deshalb klickt am Ende ein Mensch.
    /// </remarks>
    public async Task<List<MatchSuggestion>> SuggestAsync(
        Guid transactionId, CancellationToken token = default)
    {
        BankTransaction? transaction = await context.BankTransactions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == transactionId, token);

        if (transaction is null)
        {
            throw new BankException("Den Umsatz gibt es nicht.");
        }

        decimal amount = Math.Abs(transaction.Amount);
        DateOnly from = transaction.BookedOn.AddDays(-MatchWindowDays);
        DateOnly to = transaction.BookedOn.AddDays(MatchWindowDays);

        List<Voucher> candidates = await context.Vouchers
            .Include(v => v.Contact)
            .Where(v => v.GrossAmount == amount && v.Date >= from && v.Date <= to)
            .AsNoTracking()
            .ToListAsync(token);

        List<MatchSuggestion> suggestions = [];

        foreach (Voucher voucher in candidates)
        {
            int distance = Math.Abs(voucher.Date.DayNumber - transaction.BookedOn.DayNumber);
            List<string> reasons = ["gleicher Betrag"];
            int score = 50;

            // Je näher am Buchungstag, desto wahrscheinlicher gehört es zusammen.
            score += Math.Max(0, 30 - distance);

            if (distance == 0)
            {
                reasons.Add("gleicher Tag");
            }
            else
            {
                reasons.Add($"{distance} Tage auseinander");
            }

            if (Mentions(transaction, voucher.Contact?.Name))
            {
                score += 25;
                reasons.Add("Name im Verwendungszweck");
            }

            if (Mentions(transaction, voucher.ExternalNumber))
            {
                score += 30;
                reasons.Add("Rechnungsnummer im Verwendungszweck");
            }

            if (voucher.ContactId is not null
                && transaction.CounterpartyIban is not null
                && await HasIbanAsync(voucher.ContactId.Value, transaction.CounterpartyIban, token))
            {
                score += 40;
                reasons.Add("IBAN des Lieferanten");
            }

            suggestions.Add(new MatchSuggestion(
                voucher.Id, voucher.Label, voucher.Description, voucher.GrossAmount,
                voucher.Date, score, string.Join(" · ", reasons)));
        }

        return [.. suggestions.OrderByDescending(s => s.Score).ThenBy(s => s.Date).Take(10)];
    }

    public async Task AssignAsync(
        Guid transactionId, Guid? voucherId, Guid? journalEntryId,
        CancellationToken token = default)
    {
        BankTransaction? transaction = await context.BankTransactions
            .FirstOrDefaultAsync(t => t.Id == transactionId, token)
            ?? throw new BankException("Den Umsatz gibt es nicht.");

        if (voucherId is { } voucher
            && !await context.Vouchers.AnyAsync(v => v.Id == voucher, token))
        {
            throw new BankException("Den Beleg gibt es nicht.");
        }

        if (journalEntryId is { } entry
            && !await context.JournalEntries.AnyAsync(e => e.Id == entry, token))
        {
            throw new BankException("Die Buchung gibt es nicht.");
        }

        transaction.VoucherId = voucherId;
        transaction.JournalEntryId = journalEntryId;
        await context.SaveChangesAsync(token);
    }

    private async Task<bool> HasIbanAsync(Guid contactId, string iban, CancellationToken token)
    {
        string wanted = BankAccount.Normalize(iban);

        string? stored = await context.ContactBankDetails
            .Where(d => d.ContactId == contactId)
            .Select(d => d.Iban)
            .FirstOrDefaultAsync(token);

        return stored is not null && BankAccount.Normalize(stored) == wanted;
    }

    /// <summary>Ob der Verwendungszweck oder die Gegenseite den Text nennt.</summary>
    private static bool Mentions(BankTransaction transaction, string? needle)
    {
        if (string.IsNullOrWhiteSpace(needle) || needle.Length < 3)
        {
            return false;
        }

        string haystack =
            $"{transaction.Purpose} {transaction.CounterpartyName} {transaction.EndToEndId}";

        return haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<BankAccount> TrackedAsync(Guid id, CancellationToken token) =>
        await context.BankAccounts.FirstOrDefaultAsync(a => a.Id == id, token)
        ?? throw new BankException("Das Konto gibt es nicht.");

    private async Task ValidateAsync(BankAccount account, CancellationToken token)
    {
        List<string> problems = [.. account.Validate()];

        if (problems.Count == 0 && await context.BankAccounts.AnyAsync(
                a => a.Iban == account.Iban && a.Id != account.Id, token))
        {
            problems.Add($"{account.IbanGrouped} ist schon als Konto angelegt.");
        }

        if (account.AccountId is { } sachkonto
            && !await context.Accounts.AnyAsync(a => a.Id == sachkonto, token))
        {
            problems.Add("Das Sachkonto gibt es nicht.");
        }

        if (problems.Count > 0)
        {
            throw new BankException(problems);
        }
    }
}
