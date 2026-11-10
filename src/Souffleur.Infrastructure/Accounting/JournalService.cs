using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Souffleur.Domain.Accounting;
using Souffleur.Domain.Organizations;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Accounting;

/// <summary>
/// Schreibt ins Journal. Einziger Weg, eine Buchung anzulegen, festzuschreiben
/// oder zu stornieren -- damit Nummernvergabe und Pruefungen nicht umgangen werden.
/// </summary>
public class JournalService(SouffleurDbContext context, TimeProvider timeProvider, ICurrentUser currentUser)
{
    /// <summary>
    /// Legt eine Buchung als Entwurf an und vergibt die naechste Journalnummer.
    /// </summary>
    public async Task<JournalEntry> AddAsync(JournalEntry entry, CancellationToken cancellationToken = default)
    {
        AccountingSettings settings = await LoadSettingsAsync(cancellationToken);
        await AttachAccountsAsync(entry, cancellationToken);

        IReadOnlyList<string> problems = entry.Validate(settings);
        if (problems.Count > 0)
        {
            throw new JournalException(problems);
        }

        // BEGIN IMMEDIATE: Die Schreibsperre faellt sofort, nicht erst beim ersten
        // Schreibzugriff. Sonst koennten zwei gleichzeitige Buchungen dieselbe
        // Nummer lesen und eine von beiden liefe beim Speichern auf den eindeutigen
        // Index -- lueckenlos waere die Reihe dann trotzdem nicht.
        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);

        entry.Number = await NextNumberAsync(entry.FiscalYear, cancellationToken);
        context.JournalEntries.Add(entry);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return entry;
    }

    /// <summary>
    /// Schreibt eine Buchung fest, und mit ihr den Beleg, auf dem sie beruht.
    /// Danach sind beide unveraenderlich.
    /// </summary>
    public async Task CommitAsync(Guid entryId, CancellationToken cancellationToken = default)
    {
        JournalEntry entry = await LoadAsync(entryId, cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();

        entry.Commit(now, currentUser.Id, await LoadSettingsAsync(cancellationToken));

        // Beleg und Buchung gehoeren zusammen -- einen Beleg nach der Buchung noch
        // aendern zu koennen, waere eine Luecke in der Nachvollziehbarkeit.
        List<Voucher> vouchers = await context.Vouchers
            .Where(v => v.JournalEntryId == entryId && v.CommittedAt == null)
            .ToListAsync(cancellationToken);

        foreach (Voucher voucher in vouchers)
        {
            voucher.Commit(now);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Storniert eine Buchung durch eine Gegenbuchung. Die urspruengliche bleibt
    /// unveraendert im Journal stehen; geloescht wird nie.
    /// </summary>
    public async Task<JournalEntry> ReverseAsync(
        Guid entryId,
        DateOnly date,
        string reason,
        CancellationToken cancellationToken = default)
    {
        JournalEntry original = await LoadAsync(entryId, cancellationToken);

        if (original.IsReversal)
        {
            throw new JournalException(["Eine Stornobuchung lässt sich nicht noch einmal stornieren."]);
        }

        bool alreadyReversed = await context.JournalEntries
            .AnyAsync(e => e.ReversalOfId == entryId, cancellationToken);
        if (alreadyReversed)
        {
            throw new JournalException([$"Buchung {original.Number} wurde bereits storniert."]);
        }

        JournalEntry reversal = original.CreateReversal(date, date.Year, reason);
        return await AddAsync(reversal, cancellationToken);
    }

    /// <summary>
    /// Die naechste freie Nummer des Geschaeftsjahres. Faengt bei 1 an.
    /// </summary>
    private async Task<int> NextNumberAsync(int fiscalYear, CancellationToken cancellationToken)
    {
        int highest = await context.JournalEntries
            .Where(e => e.FiscalYear == fiscalYear)
            .Select(e => (int?)e.Number)
            .MaxAsync(cancellationToken) ?? 0;

        return highest + 1;
    }

    private async Task<JournalEntry> LoadAsync(Guid entryId, CancellationToken cancellationToken)
    {
        JournalEntry? entry = await context.JournalEntries
            .Include(e => e.Lines).ThenInclude(l => l.Account)
            .FirstOrDefaultAsync(e => e.Id == entryId, cancellationToken);

        return entry ?? throw new JournalException(["Diese Buchung gibt es nicht."]);
    }

    /// <summary>
    /// Die Regeln dieses Hauses. Solange die Stammdaten fehlen, gelten die
    /// Vorgaben -- dann wird die Sphaerenpflicht nicht geprueft.
    /// </summary>
    private async Task<AccountingSettings> LoadSettingsAsync(CancellationToken cancellationToken)
    {
        Organization? organization = await context.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        return organization is null
            ? AccountingSettings.Default
            : new AccountingSettings(organization.ChartOfAccounts, organization.RequiresTaxSpheres);
    }

    /// <summary>
    /// Haengt die Konten an die Zeilen, damit die Pruefung die Kontenart kennt.
    /// Ohne das liesse sich nicht sagen, ob eine Zeile eine Sphaere braucht.
    /// </summary>
    private async Task AttachAccountsAsync(JournalEntry entry, CancellationToken cancellationToken)
    {
        Guid[] accountIds = [.. entry.Lines.Select(l => l.AccountId).Distinct()];

        Dictionary<Guid, Account> accounts = await context.Accounts
            .Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, cancellationToken);

        foreach (JournalLine line in entry.Lines)
        {
            if (accounts.TryGetValue(line.AccountId, out Account? account))
            {
                line.Account = account;

                // Vorschlag des Kontos uebernehmen, wo die Buchung nichts sagt.
                line.TaxSphere ??= account.DefaultTaxSphere;
            }
        }
    }
}

/// <summary>Eine Buchung wurde abgelehnt. Die Gruende stehen in <see cref="Problems"/>.</summary>
public class JournalException(IReadOnlyList<string> problems)
    : InvalidOperationException(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}
