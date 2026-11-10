using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Contacts;
using Souffleur.Domain.Identity;
using Souffleur.Infrastructure.Contacts;
using Souffleur.Infrastructure.Identity;
using Souffleur.Api.Infrastructure;

namespace Souffleur.Api.Features.Contacts;

/// <summary>
/// Das Adressbuch des Hauses -- die eine Stelle, an der steht, wer mit dem Haus
/// zu tun hat. Andere Module verweisen hierher, statt eigene Adressen zu führen.
/// </summary>
public static class ContactEndpoints
{
    public static IEndpointRouteBuilder MapContactEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/contacts").WithTags("Kontakte");

        // Kein RequireModule: Kontakte lassen sich nicht abschalten, weil die
        // anderen Module daran hängen.
        group.MapGet("/", ListAsync).RequireAuthorization();
        group.MapGet("/roles", ListRoles).RequireAuthorization();
        group.MapGet("/accounts", AccountsAsync)
            .RequirePermission(Permission.AccountsManage);
        group.MapGet("/{id:guid}", GetAsync).RequireAuthorization();
        group.MapGet("/{id:guid}/usage", UsageAsync).RequireAuthorization();
        group.MapPost("/", CreateAsync).RequireAuthorization(Write);
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(Write);
        group.MapPost("/{id:guid}/deactivate", DeactivateAsync).RequireAuthorization(Write);
        group.MapPost("/{id:guid}/withdraw-consent", WithdrawConsentAsync).RequireAuthorization(Write);

        return app;
    }

    private static void Write(Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder policy) =>
        policy.RequireClaim(PermissionClaimsFactory.ClaimType, ((int)Permission.ContactsWrite).ToString());

    private static async Task<IResult> ListAsync(
        ContactService contacts,
        [FromQuery] string? search = null,
        [FromQuery] ContactRole? role = null,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100)
    {
        IQueryable<Contact> query = contacts.Query().AsNoTracking();

        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        if (role is { } wanted)
        {
            query = query.Where(c => c.Roles.Any(r => r.Role == wanted));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            query = query.Where(c =>
                EF.Functions.Like(c.Name, $"%{term}%")
                || (c.Email != null && EF.Functions.Like(c.Email, $"%{term}%"))
                || (c.City != null && EF.Functions.Like(c.City, $"%{term}%")));
        }

        List<Contact> found = await query
            .OrderBy(c => c.Name)
            .Skip(skip)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync();

        return TypedResults.Ok(found.Select(ContactResponse.From));
    }

    private static IResult ListRoles() =>
        TypedResults.Ok(ContactRoles.All.Select(role => new RoleResponse(
            role, role.ToString(), role.Label(), role.NeedsConsent(), role.NeedsBankDetails())));

    private static async Task<IResult> GetAsync(Guid id, ContactService contacts)
    {
        Contact? contact = await contacts.Query().AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        return contact is null ? TypedResults.NotFound() : TypedResults.Ok(ContactResponse.From(contact));
    }

    private static async Task<IResult> UsageAsync(Guid id, ContactService contacts) =>
        TypedResults.Ok(await contacts.UsageAsync(id));

    /// <summary>
    /// Die Anmeldekonten des Hauses, zum Verbinden mit einem Kontakt.
    /// </summary>
    /// <remarks>
    /// Nur fuer die Verwaltung: Eine Liste aller Konten samt Adressen ist
    /// nichts, was jeder Angemeldete braucht.
    /// </remarks>
    private static async Task<IResult> AccountsAsync(
        ContactService contacts,
        CancellationToken token) =>
        TypedResults.Ok(await contacts.AccountsAsync(token));

    private static async Task<IResult> CreateAsync(
        [FromBody] ContactRequest request,
        ContactService contacts,
        HttpContext http)
    {
        var contact = new Contact { Kind = request.Kind, Name = request.Name ?? string.Empty };
        Apply(request, contact, http.User.IsInRole(Roles.Administrator));

        try
        {
            await contacts.AddAsync(contact);
        }
        catch (ContactException exception)
        {
            return Rejected(exception);
        }

        return TypedResults.Created($"/api/contacts/{contact.Id}", ContactResponse.From(contact));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] ContactRequest request,
        ContactService contacts,
        HttpContext http)
    {
        bool mayLinkAccount = http.User.IsInRole(Roles.Administrator);

        Contact updated;
        try
        {
            updated = await contacts.UpdateAsync(id, contact =>
            {
                contact.Kind = request.Kind;
                Apply(request, contact, mayLinkAccount);
            });
        }
        catch (ContactException exception)
        {
            return Rejected(exception);
        }

        return TypedResults.Ok(ContactResponse.From(updated));
    }

    private static async Task<IResult> DeactivateAsync(Guid id, ContactService contacts)
    {
        try
        {
            await contacts.DeactivateAsync(id);
        }
        catch (ContactException exception)
        {
            return Rejected(exception);
        }

        return TypedResults.NoContent();
    }

    private static async Task<IResult> WithdrawConsentAsync(Guid id, ContactService contacts)
    {
        try
        {
            await contacts.WithdrawConsentAsync(id);
        }
        catch (ContactException exception)
        {
            return Rejected(exception);
        }

        return TypedResults.NoContent();
    }

    /// <summary>
    /// Was aus der Anfrage in den Kontakt geht. Das Benutzerkonto nur, wenn die
    /// Verwaltung fragt -- wer sich selbst ein Konto zuordnen koennte, koennte
    /// sich fremde Zeiten zuordnen.
    /// </summary>
    private static void Apply(ContactRequest request, Contact contact, bool mayLinkAccount)
    {
        contact.Name = request.Name ?? contact.Name;
        contact.GivenName = request.GivenName;
        contact.Surname = request.Surname;
        contact.FormOfAddress = request.FormOfAddress;
        contact.Position = request.Position;
        contact.OrganizationContactId = request.OrganizationContactId;
        contact.Email = request.Email;
        contact.Phone = request.Phone;
        contact.Website = request.Website;
        contact.Street = request.Street;
        contact.PostalCode = request.PostalCode;
        contact.City = request.City;
        contact.Country = request.Country;
        contact.Notes = request.Notes;
        contact.Origin = request.Origin;
        contact.ContactConsentAt = request.ContactConsentAt;

        if (mayLinkAccount)
        {
            contact.UserId = request.UserId;
        }

        // Rollen abgleichen statt neu anlegen, damit Notizen daran erhalten bleiben.
        contact.Roles.RemoveAll(existing => !request.Roles.Contains(existing.Role));
        foreach (ContactRole role in request.Roles.Distinct())
        {
            if (!contact.HasRole(role))
            {
                contact.Roles.Add(new ContactRoleAssignment { Role = role });
            }
        }

        if (request.BankDetails is { } bank)
        {
            contact.BankDetails ??= new ContactBankDetails();
            contact.BankDetails.Iban = bank.Iban;
            contact.BankDetails.Bic = bank.Bic;
            contact.BankDetails.AccountHolder = bank.AccountHolder;
            contact.BankDetails.VatId = bank.VatId;
            contact.BankDetails.TaxNumber = bank.TaxNumber;
            contact.BankDetails.PaymentTermDays = bank.PaymentTermDays;
            contact.BankDetails.AccountNumber = bank.AccountNumber;
            contact.BankDetails.IsSubjectToArtistsSocialFund = bank.IsSubjectToArtistsSocialFund;
        }
    }

    private static IResult Rejected(ContactException exception) =>
        TypedResults.ValidationProblem(
            new Dictionary<string, string[]> { ["Kontakt"] = [.. exception.Problems] },
            title: "Der Kontakt wurde nicht angenommen.");
}

public record BankDetailsRequest(
    string? Iban = null,
    string? Bic = null,
    string? AccountHolder = null,
    string? VatId = null,
    string? TaxNumber = null,
    int? PaymentTermDays = null,
    string? AccountNumber = null,
    bool IsSubjectToArtistsSocialFund = false);

public record ContactRequest(
    ContactKind Kind,
    IReadOnlyList<ContactRole> Roles,
    string? Name = null,
    string? GivenName = null,
    string? Surname = null,
    string? FormOfAddress = null,
    string? Position = null,
    Guid? OrganizationContactId = null,
    string? Email = null,
    string? Phone = null,
    string? Website = null,
    string? Street = null,
    string? PostalCode = null,
    string? City = null,
    string? Country = null,
    string? Notes = null,
    string? Origin = null,
    DateTimeOffset? ContactConsentAt = null,
    Guid? UserId = null,
    BankDetailsRequest? BankDetails = null);

public record ContactResponse(
    Guid Id,
    ContactKind Kind,
    string Name,
    string? GivenName,
    string? Surname,
    string? Position,
    Guid? OrganizationContactId,
    string? Email,
    string? Phone,
    string? Website,
    string? Street,
    string? PostalCode,
    string? City,
    string? Country,
    string? Notes,
    string? Origin,
    bool IsActive,
    bool MayBeContacted,
    DateTimeOffset? ContactConsentAt,
    DateTimeOffset? ContactConsentWithdrawnAt,
    Guid? UserId,
    IReadOnlyList<RoleOnContact> Roles,
    BankDetailsRequest? BankDetails)
{
    public static ContactResponse From(Contact c) => new(
        c.Id, c.Kind, c.Name, c.GivenName, c.Surname, c.Position, c.OrganizationContactId,
        c.Email, c.Phone, c.Website, c.Street, c.PostalCode, c.City, c.Country, c.Notes,
        c.Origin, c.IsActive, c.MayBeContacted, c.ContactConsentAt, c.ContactConsentWithdrawnAt,
        c.UserId,
        [.. c.Roles.Select(r => new RoleOnContact(r.Role, r.Role.ToString(), r.Role.Label(), r.Note))],
        c.BankDetails is null
            ? null
            : new BankDetailsRequest(
                c.BankDetails.Iban, c.BankDetails.Bic, c.BankDetails.AccountHolder,
                c.BankDetails.VatId, c.BankDetails.TaxNumber, c.BankDetails.PaymentTermDays,
                c.BankDetails.AccountNumber, c.BankDetails.IsSubjectToArtistsSocialFund));
}

public record RoleOnContact(ContactRole Role, string Key, string Label, string? Note);

public record RoleResponse(
    ContactRole Role, string Key, string Label, bool NeedsConsent, bool NeedsBankDetails);
