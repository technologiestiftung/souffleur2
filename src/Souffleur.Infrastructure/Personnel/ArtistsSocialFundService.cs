using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Accounting;
using Souffleur.Domain.Contacts;
using Souffleur.Domain.Personnel;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Personnel;

public class ArtistsSocialFundException(IReadOnlyList<string> problems)
    : Exception(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;

    public ArtistsSocialFundException(string problem) : this([problem])
    {
    }
}

/// <summary>
/// Die Kuenstlersozialabgabe: was das Haus im Jahr an gekennzeichnete Personen
/// gezahlt hat, und was daraus folgt.
/// </summary>
/// <remarks>
/// Gerechnet wird aus den Belegen, nicht aus den Buchungen: Ein Beleg kann
/// erfasst und noch nicht gebucht sein, und bis zum Meldeschluss im Maerz ist
/// das der Normalfall. Wo eine Buchung dranhaengt, zieht die Rechnung die
/// ausgewiesene Steuer ab -- Bemessungsgrundlage ist das Entgelt, nicht der
/// Bruttobetrag. Wo keine haengt, steht brutto, und der Beleg wird gezaehlt,
/// damit die Zahl nicht genauer aussieht, als sie ist.
/// </remarks>
public class ArtistsSocialFundService(SouffleurDbContext context)
{
    public Task<List<ArtistsSocialFundRate>> RatesAsync(CancellationToken token = default) =>
        context.ArtistsSocialFundRates
            .AsNoTracking()
            .OrderByDescending(r => r.Year)
            .ToListAsync(token);

    /// <summary>Den Satz eines Jahres setzen oder aendern.</summary>
    public async Task<ArtistsSocialFundRate> SetRateAsync(
        int year,
        decimal percent,
        CancellationToken token = default)
    {
        ArtistsSocialFundRate? rate = await context.ArtistsSocialFundRates
            .FirstOrDefaultAsync(r => r.Year == year, token);

        if (rate is null)
        {
            rate = new ArtistsSocialFundRate { Year = year, Percent = percent };
            context.ArtistsSocialFundRates.Add(rate);
        }
        else
        {
            rate.Percent = percent;
        }

        IReadOnlyList<string> problems = rate.Validate();

        if (problems.Count > 0)
        {
            throw new ArtistsSocialFundException(problems);
        }

        await context.SaveChangesAsync(token);
        return rate;
    }

    public async Task<ArtistsSocialFundReport> ReportAsync(
        int year,
        CancellationToken token = default)
    {
        decimal? percent = await context.ArtistsSocialFundRates
            .Where(r => r.Year == year)
            .Select(r => (decimal?)r.Percent)
            .FirstOrDefaultAsync(token);

        // Alles, was das Haus im Jahr an Personen gezahlt hat. Eingang heisst:
        // Das Haus zahlt.
        var payments = await context.Vouchers
            .AsNoTracking()
            .Where(v => v.FiscalYear == year
                && v.Direction == VoucherDirection.Incoming
                && v.ContactId != null)
            .Select(v => new
            {
                ContactId = v.ContactId!.Value,
                v.Contact!.Name,
                Kind = v.Contact.Kind,
                Subject = v.Contact.BankDetails != null
                    && v.Contact.BankDetails.IsSubjectToArtistsSocialFund,
                IsArtist = v.Contact.Roles.Any(r => r.Role == ContactRole.Artist),
                v.GrossAmount,
                Tax = v.JournalEntry == null
                    ? (decimal?)null
                    : v.JournalEntry.Lines.Sum(l => l.TaxAmount),
            })
            .ToListAsync(token);

        List<ArtistPayments> Summarize(bool subject) =>
        [
            .. payments
                .Where(p => p.Subject == subject)
                .GroupBy(p => new { p.ContactId, p.Name })
                .Select(group => new ArtistPayments(
                    group.Key.ContactId,
                    group.Key.Name,
                    group.Sum(p => p.GrossAmount - (p.Tax ?? 0m)),
                    group.Count(),
                    group.Count(p => p.Tax is null)))
                .OrderByDescending(a => a.Amount),
        ];

        List<ArtistPayments> flagged = Summarize(subject: true);

        // Wer kuenstlerisch mit dem Haus zu tun hat, Geld bekommen hat und
        // nicht gekennzeichnet ist. Eine Organisation steht nicht dabei: Die
        // Abgabe faellt auf Entgelte an selbstaendige Personen an.
        List<ArtistPayments> unflagged =
        [
            .. payments
                .Where(p => !p.Subject && p.IsArtist && p.Kind == ContactKind.Person)
                .GroupBy(p => new { p.ContactId, p.Name })
                .Select(group => new ArtistPayments(
                    group.Key.ContactId,
                    group.Key.Name,
                    group.Sum(p => p.GrossAmount - (p.Tax ?? 0m)),
                    group.Count(),
                    group.Count(p => p.Tax is null)))
                .OrderByDescending(a => a.Amount),
        ];

        decimal total = flagged.Sum(a => a.Amount);

        return new ArtistsSocialFundReport(
            year,
            percent,
            total,
            percent is { } rate ? Math.Round(total * rate / 100m, 2) : null,
            flagged,
            unflagged);
    }
}
