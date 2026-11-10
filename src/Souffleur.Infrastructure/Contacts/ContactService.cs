using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Identity;
using Souffleur.Domain.Contacts;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Contacts;

/// <summary>
/// Das Adressbuch. Einziger Weg, einen Kontakt anzulegen oder zu ändern --
/// damit der Anzeigename stimmt und die Prüfungen nicht umgangen werden.
/// </summary>
public class ContactService(SouffleurDbContext context, TimeProvider timeProvider)
{
    public IQueryable<Contact> Query() =>
        context.Contacts.Include(c => c.Roles).Include(c => c.BankDetails);

    /// <summary>
    /// Die Anmeldekonten des Hauses, mit dem Kontakt, an dem sie schon haengen.
    /// </summary>
    /// <remarks>
    /// Ein Konto gehoert hoechstens einem Kontakt. Wer schon verbunden ist,
    /// steht trotzdem in der Liste -- sonst sieht die Verwaltung nicht, warum
    /// ein Konto fehlt.
    /// </remarks>
    public async Task<List<UserAccount>> AccountsAsync(CancellationToken cancellationToken = default)
    {
        Dictionary<Guid, string> taken = await context.Contacts
            .Where(c => c.UserId != null)
            .ToDictionaryAsync(c => c.UserId!.Value, c => c.Name, cancellationToken);

        return await context.Users
            .OrderBy(u => u.Surname).ThenBy(u => u.GivenName)
            .Select(u => new UserAccount(
                u.Id,
                u.GivenName + " " + u.Surname,
                u.Email,
                u.IsActive,
                taken.ContainsKey(u.Id) ? taken[u.Id] : null))
            .ToListAsync(cancellationToken);
    }

    public async Task<Contact> AddAsync(Contact contact, CancellationToken cancellationToken = default)
    {
        Prepare(contact);

        IReadOnlyList<string> problems = contact.Validate();
        if (problems.Count > 0)
        {
            throw new ContactException(problems);
        }

        context.Contacts.Add(contact);
        await context.SaveChangesAsync(cancellationToken);
        return contact;
    }

    public async Task<Contact> UpdateAsync(
        Guid id,
        Action<Contact> change,
        CancellationToken cancellationToken = default)
    {
        Contact contact = await Query().FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new ContactException(["Diesen Kontakt gibt es nicht."]);

        change(contact);
        Prepare(contact);

        IReadOnlyList<string> problems = contact.Validate();
        if (problems.Count > 0)
        {
            throw new ContactException(problems);
        }

        await context.SaveChangesAsync(cancellationToken);
        return contact;
    }

    /// <summary>
    /// Zieht einen Kontakt aus dem Verkehr. Gelöscht wird nicht, solange
    /// Buchungen oder Belege auf ihn verweisen -- und das tun sie
    /// aufbewahrungspflichtig acht Jahre lang.
    /// </summary>
    public async Task DeactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Contact contact = await Query().FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new ContactException(["Diesen Kontakt gibt es nicht."]);

        contact.IsActive = false;
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Nimmt die Einwilligung zurück. Ab jetzt schreibt das Haus nicht mehr an.
    /// </summary>
    public Task WithdrawConsentAsync(Guid id, CancellationToken cancellationToken = default) =>
        UpdateAsync(id, c => c.ContactConsentWithdrawnAt = timeProvider.GetUtcNow(), cancellationToken);

    /// <summary>
    /// Wo ein Kontakt überall auftaucht. Vor dem Löschen zu fragen -- und wenn
    /// jemand nach Auskunft verlangt (Art. 15 DSGVO).
    /// </summary>
    public async Task<ContactUsage> UsageAsync(Guid id, CancellationToken cancellationToken = default) =>
        new(
            await context.JournalLines.CountAsync(l => l.ContactId == id, cancellationToken),
            await context.Vouchers.CountAsync(v => v.ContactId == id, cancellationToken),
            await context.Contacts.CountAsync(c => c.OrganizationContactId == id, cancellationToken));

    /// <summary>
    /// Setzt, was sich aus den Eingaben ergibt: Anzeigename, und Bankdaten weg,
    /// wo keine Rolle sie braucht.
    /// </summary>
    private static void Prepare(Contact contact)
    {
        contact.UpdateName();

        bool needsBank = contact.Roles.Any(r => r.Role.NeedsBankDetails());
        if (!needsBank && contact.BankDetails is { } details && IsEmpty(details))
        {
            contact.BankDetails = null;
        }
    }

    private static bool IsEmpty(ContactBankDetails details) =>
        string.IsNullOrWhiteSpace(details.Iban)
        && string.IsNullOrWhiteSpace(details.Bic)
        && string.IsNullOrWhiteSpace(details.VatId)
        && string.IsNullOrWhiteSpace(details.TaxNumber)
        && string.IsNullOrWhiteSpace(details.AccountNumber)
        && details.PaymentTermDays is null
        && !details.IsSubjectToArtistsSocialFund;
}

public record ContactUsage(int JournalLines, int Vouchers, int People)
{
    public bool IsReferenced => JournalLines > 0 || Vouchers > 0 || People > 0;
}

public class ContactException(IReadOnlyList<string> problems)
    : InvalidOperationException(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>Ein Anmeldekonto, so wie die Verwaltung es auswaehlt.</summary>
public record UserAccount(Guid Id, string Name, string? Email, bool IsActive, string? LinkedTo);
