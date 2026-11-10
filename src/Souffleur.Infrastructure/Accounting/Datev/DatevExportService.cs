using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Souffleur.Domain.Accounting;
using Souffleur.Domain.Organizations;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Accounting.Datev;

/// <summary>
/// Übergibt einen Zeitraum ans Steuerbüro.
/// </summary>
/// <remarks>
/// Der Export ist kein Lesevorgang: Mit ihm werden die enthaltenen Buchungen
/// festgeschrieben. Ab da sind sie unveraenderlich, und Korrekturen laufen als
/// Storno im Folgemonat. Deshalb wird jeder Export protokolliert -- mit Zeitraum,
/// Anzahl, Summe und Pruefwert der Datei.
/// </remarks>
public class DatevExportService(
    SouffleurDbContext context,
    IOptions<DatevOptions> options,
    TimeProvider timeProvider,
    ICurrentUser currentUser)
{
    /// <summary>
    /// Sieht nach, was ein Export umfassen wuerde, ohne etwas zu veraendern.
    /// </summary>
    public async Task<DatevPreview> PreviewAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        List<JournalEntry> entries = await LoadEntriesAsync(from, to, cancellationToken);

        return new DatevPreview(
            from,
            to,
            entries.Count,
            entries.Count(e => e.IsCommitted),
            entries.Sum(e => e.DebitTotal),
            [.. options.Value.Problems(), .. await ReadinessProblemsAsync(from, to, entries, cancellationToken)]);
    }

    /// <summary>
    /// Erzeugt den Stapel und schreibt die enthaltenen Buchungen fest.
    /// </summary>
    public async Task<DatevResult> ExportAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        DatevOptions settings = options.Value;
        List<string> problems = [.. settings.Problems()];

        Organization? organization = await context.Organizations.FirstOrDefaultAsync(cancellationToken);
        if (organization is null)
        {
            problems.Add("Die Stammdaten des Hauses fehlen. Ohne sie lässt sich kein Stapel erzeugen.");
        }

        List<JournalEntry> entries = await LoadEntriesAsync(from, to, cancellationToken);
        problems.AddRange(await ReadinessProblemsAsync(from, to, entries, cancellationToken));

        if (problems.Count > 0)
        {
            throw new DatevException(problems);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        byte[] content = new DatevBuchungsstapel(settings)
            .Build(entries, organization!, from, to, now);

        string fileName = $"EXTF_Buchungsstapel_{from:yyyyMMdd}_{to:yyyyMMdd}.csv";
        string hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

        // Erst ab hier wird etwas verändert: festschreiben und protokollieren.
        foreach (JournalEntry entry in entries.Where(e => !e.IsCommitted))
        {
            entry.Commit(now, currentUser.Id);
        }

        foreach (Voucher voucher in await context.Vouchers
                     .Where(v => v.CommittedAt == null
                                 && v.JournalEntryId != null
                                 && entries.Select(e => e.Id).Contains(v.JournalEntryId.Value))
                     .ToListAsync(cancellationToken))
        {
            voucher.Commit(now);
        }

        var record = new DatevExport
        {
            FiscalYear = from.Year,
            From = from,
            To = to,
            FileName = fileName,
            EntryCount = entries.Count,
            DebitTotal = entries.Sum(e => e.DebitTotal),
            Sha256 = hash,
            ExportedAt = now,
            ExportedBy = currentUser.Id,
        };

        context.DatevExports.Add(record);
        await context.SaveChangesAsync(cancellationToken);

        return new DatevResult(record, content);
    }

    public Task<List<DatevExport>> HistoryAsync(CancellationToken cancellationToken = default) =>
        context.DatevExports.AsNoTracking()
            .OrderByDescending(e => e.ExportedAt)
            .ToListAsync(cancellationToken);

    private Task<List<JournalEntry>> LoadEntriesAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        context.JournalEntries
            .Include(e => e.Lines).ThenInclude(l => l.Account)
            .Include(e => e.Lines).ThenInclude(l => l.CostCenter)
            .Where(e => e.Date >= from && e.Date <= to)
            .OrderBy(e => e.Number)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Was einem Export im Weg steht. Lieber hier abbrechen als dem Steuerbüro
    /// einen halben Monat schicken.
    /// </summary>
    private async Task<IReadOnlyList<string>> ReadinessProblemsAsync(
        DateOnly from,
        DateOnly to,
        IReadOnlyList<JournalEntry> entries,
        CancellationToken cancellationToken)
    {
        List<string> problems = [];

        if (from > to)
        {
            problems.Add("Der Zeitraum endet vor seinem Beginn.");
        }

        if (from.Year != to.Year)
        {
            problems.Add("Ein Stapel umfasst keinen Jahreswechsel; Wirtschaftsjahre werden einzeln übergeben.");
        }

        if (entries.Count == 0)
        {
            problems.Add($"Im Zeitraum {from:dd.MM.yyyy} bis {to:dd.MM.yyyy} gibt es keine Buchungen.");
        }

        // Ein bereits übergebener Zeitraum darf nicht zweimal gehen -- das
        // Steuerbüro bekäme die Buchungen doppelt.
        DatevExport? overlapping = await context.DatevExports
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.From <= to && e.To >= from, cancellationToken);

        if (overlapping is not null)
        {
            problems.Add(
                $"Der Zeitraum überschneidet sich mit der Übergabe {overlapping.Label} "
                + $"vom {overlapping.ExportedAt:dd.MM.yyyy}.");
        }

        return problems;
    }
}

public record DatevPreview(
    DateOnly From,
    DateOnly To,
    int EntryCount,
    int AlreadyCommitted,
    decimal DebitTotal,
    IReadOnlyList<string> Problems)
{
    public bool CanExport => Problems.Count == 0;
}

public record DatevResult(DatevExport Record, byte[] Content);

public class DatevException(IReadOnlyList<string> problems)
    : InvalidOperationException(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}
