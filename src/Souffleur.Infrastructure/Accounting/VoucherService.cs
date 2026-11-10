using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Souffleur.Domain.Accounting;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Accounting;

/// <summary>
/// Legt Belege an und verbindet sie mit ihrer Buchung.
/// </summary>
public class VoucherService(
    SouffleurDbContext context,
    VoucherStorage storage,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Nimmt einen Beleg auf: Nummer vergeben, Datei ablegen, speichern.
    /// Schlaegt die Datei fehl, wird auch der Beleg nicht angelegt.
    /// </summary>
    public async Task<Voucher> AddAsync(
        Voucher voucher,
        Stream? content = null,
        string? originalName = null,
        string? contentType = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> problems = voucher.Validate();
        if (problems.Count > 0)
        {
            throw new VoucherException(problems);
        }

        await using IDbContextTransaction transaction = await context.Database
            .BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);

        voucher.Number = await NextNumberAsync(voucher.FiscalYear, cancellationToken);
        context.Vouchers.Add(voucher);

        if (content is not null)
        {
            voucher.File = await storage.StoreAsync(
                voucher,
                content,
                originalName ?? "beleg",
                contentType ?? "application/pdf",
                cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return voucher;
    }

    /// <summary>
    /// Aendert die Angaben um den Beleg herum. Die Datei bleibt, wie sie kam.
    /// </summary>
    /// <remarks>
    /// Erlaubt, solange der Beleg ein Entwurf ist. Danach greift die
    /// Unveraenderbarkeit (§ 146 Abs. 4 AO): Eine Korrektur geht dann nur noch
    /// ueber einen neuen Beleg, nicht ueber den alten.
    ///
    /// Drei Angaben bleiben auch im Entwurf aussen vor. Die Belegnummer und ihr
    /// Geschaeftsjahr, weil sie lueckenlos je Jahr vergeben sind -- ein Beleg,
    /// der das Jahr wechselt, hinterliesse im alten eine Luecke, die niemand
    /// mehr erklaeren kann. Und die Herkunft: Ob ein Beleg abfotografiert oder
    /// hochgeladen wurde, ist eine Tatsache und keine Angabe.
    ///
    /// Der Betrag darf sich auch dann noch aendern, wenn der Beleg bereits an
    /// einer Buchung haengt. Beide sind dann Entwuerfe, und wer den Tippfehler
    /// im Beleg sieht, sieht ihn meist erst dort.
    /// </remarks>
    public async Task<Voucher> UpdateAsync(
        Guid id,
        VoucherChanges changes,
        CancellationToken cancellationToken = default)
    {
        Voucher voucher = await LoadAsync(id, cancellationToken);

        if (voucher.CommittedAt is { } committedAt)
        {
            throw new VoucherException(
                [$"Beleg {voucher.Label} wurde am {committedAt:dd.MM.yyyy} festgeschrieben "
                 + "und laesst sich nicht mehr aendern."]);
        }

        if (changes.Date.Year != voucher.FiscalYear)
        {
            throw new VoucherException(
                [$"Das Belegdatum muss im Geschaeftsjahr {voucher.FiscalYear} bleiben. "
                 + $"Fuer {changes.Date.Year} braucht es einen neuen Beleg -- die Nummer "
                 + $"{voucher.Label} ist in ihrem Jahr vergeben."]);
        }

        voucher.Date = changes.Date;
        voucher.GrossAmount = changes.GrossAmount;
        voucher.Description = changes.Description;
        voucher.ExternalNumber = string.IsNullOrWhiteSpace(changes.ExternalNumber)
            ? null
            : changes.ExternalNumber.Trim();
        voucher.DueDate = changes.DueDate;
        voucher.ContactId = changes.ContactId;
        voucher.Direction = changes.Direction;
        voucher.ProductionId = changes.ProductionId;

        IReadOnlyList<string> problems = voucher.Validate();
        if (problems.Count > 0)
        {
            throw new VoucherException(problems);
        }

        await context.SaveChangesAsync(cancellationToken);

        return voucher;
    }

    /// <summary>
    /// Haengt einen Beleg an eine Buchung. Beides muss noch Entwurf sein.
    /// </summary>
    public async Task LinkAsync(Guid voucherId, Guid journalEntryId, CancellationToken cancellationToken = default)
    {
        Voucher voucher = await LoadAsync(voucherId, cancellationToken);

        JournalEntry entry = await context.JournalEntries
            .FirstOrDefaultAsync(e => e.Id == journalEntryId, cancellationToken)
            ?? throw new VoucherException(["Diese Buchung gibt es nicht."]);

        if (voucher.CommittedAt is not null)
        {
            throw new VoucherException([$"Beleg {voucher.Label} ist festgeschrieben."]);
        }

        if (entry.IsCommitted)
        {
            throw new VoucherException(
                [$"Buchung {entry.Number} ist festgeschrieben und nimmt keinen Beleg mehr an."]);
        }

        if (voucher.JournalEntryId is { } existing && existing != journalEntryId)
        {
            throw new VoucherException(
                [$"Beleg {voucher.Label} hängt bereits an einer anderen Buchung."]);
        }

        voucher.JournalEntryId = journalEntryId;
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Schreibt den Beleg fest. Wird von der Festschreibung der Buchung mit
    /// aufgerufen -- Beleg und Buchung gehoeren zusammen.
    /// </summary>
    public async Task CommitAsync(Guid voucherId, CancellationToken cancellationToken = default)
    {
        Voucher voucher = await LoadAsync(voucherId, cancellationToken);
        voucher.Commit(timeProvider.GetUtcNow());
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Prueft die abgelegten Dateien gegen ihren Pruefwert und meldet, was fehlt
    /// oder sich geaendert hat.
    /// </summary>
    public async Task<IReadOnlyList<VoucherIntegrityProblem>> CheckFilesAsync(
        int? fiscalYear = null,
        CancellationToken cancellationToken = default)
    {
        List<Voucher> vouchers = await context.Vouchers
            .Include(v => v.File)
            .Where(v => v.File != null && (fiscalYear == null || v.FiscalYear == fiscalYear))
            .ToListAsync(cancellationToken);

        List<VoucherIntegrityProblem> problems = [];

        foreach (Voucher voucher in vouchers)
        {
            VoucherFile file = voucher.File!;

            if (!storage.Exists(file))
            {
                problems.Add(new VoucherIntegrityProblem(voucher.Id, voucher.Label, "Die Datei fehlt."));
                continue;
            }

            if (!await storage.VerifyAsync(file, cancellationToken))
            {
                problems.Add(new VoucherIntegrityProblem(
                    voucher.Id, voucher.Label, "Die Datei weicht von ihrem Prüfwert ab."));
            }
        }

        return problems;
    }

    private async Task<int> NextNumberAsync(int fiscalYear, CancellationToken cancellationToken)
    {
        int highest = await context.Vouchers
            .Where(v => v.FiscalYear == fiscalYear)
            .Select(v => (int?)v.Number)
            .MaxAsync(cancellationToken) ?? 0;

        return highest + 1;
    }

    private async Task<Voucher> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Vouchers.Include(v => v.File).FirstOrDefaultAsync(v => v.Id == id, cancellationToken)
        ?? throw new VoucherException(["Diesen Beleg gibt es nicht."]);
}

/// <summary>
/// Was sich an einem Beleg nachtragen oder richtigstellen laesst.
/// </summary>
/// <remarks>
/// Bewusst alle Felder auf einmal statt einzelner Teiländerungen: Ein Beleg
/// ist ein Formular, das jemand vor sich hat. Wer das Datum korrigiert, sieht
/// dabei den Betrag -- und soll ihn mitschicken duerfen, ohne dass die
/// Schnittstelle zwischen "nicht angegeben" und "auf null gesetzt" raten muss.
/// </remarks>
public record VoucherChanges(
    DateOnly Date,
    decimal GrossAmount,
    string Description,
    string? ExternalNumber,
    DateOnly? DueDate,
    Guid? ContactId,
    VoucherDirection Direction,
    Guid? ProductionId = null);

public record VoucherIntegrityProblem(Guid VoucherId, string Label, string Problem);

public class VoucherException(IReadOnlyList<string> problems)
    : InvalidOperationException(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}
