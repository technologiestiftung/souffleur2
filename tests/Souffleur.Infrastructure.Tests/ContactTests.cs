using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Accounting;
using Souffleur.Domain.Contacts;
using Souffleur.Infrastructure.Contacts;

namespace Souffleur.Infrastructure.Tests;

/// <summary>
/// Kontakte sind die Grundlage unter den anderen Modulen: ein Adressbuch, nicht
/// eines je Modul. Diese Tests halten fest, was das bedeutet.
/// </summary>
public class ContactTests : AccountingTestBase
{
    private ContactService Service => new(Context, TimeProvider.System);

    private static Contact Supplier(string name = "Druckerei Conrad") => new()
    {
        Kind = ContactKind.Organization,
        Name = name,
        City = "Berlin",
        Roles = [new ContactRoleAssignment { Role = ContactRole.Supplier }],
    };

    private static Contact Person(string given, string surname, ContactRole role) => new()
    {
        Kind = ContactKind.Person,
        Name = string.Empty,
        GivenName = given,
        Surname = surname,
        Roles = [new ContactRoleAssignment { Role = role }],
    };

    [Fact]
    public async Task Eine_Organisation_wird_angelegt()
    {
        Contact contact = await Service.AddAsync(Supplier());

        contact.Name.Should().Be("Druckerei Conrad");
        contact.HasRole(ContactRole.Supplier).Should().BeTrue();
    }

    [Fact]
    public async Task Bei_Personen_entsteht_der_Anzeigename_aus_Vor_und_Nachname()
    {
        Contact contact = await Service.AddAsync(Person("Anja", "Behrens", ContactRole.Artist));

        // Nachgestellt, damit Listen nach Nachnamen sortieren.
        contact.Name.Should().Be("Behrens, Anja");
    }

    [Fact]
    public async Task Eine_Person_ohne_Nachnamen_wird_abgewiesen()
    {
        Contact contact = Person("Anja", string.Empty, ContactRole.Artist);

        Func<Task> act = () => Service.AddAsync(contact);

        (await act.Should().ThrowAsync<ContactException>())
            .Which.Problems.Should().Contain(p => p.Contains("Nachnamen"));
    }

    [Fact]
    public async Task Ein_Kontakt_ohne_Rolle_wird_abgewiesen()
    {
        var contact = new Contact { Kind = ContactKind.Organization, Name = "Namenlos", Roles = [] };

        Func<Task> act = () => Service.AddAsync(contact);

        (await act.Should().ThrowAsync<ContactException>())
            .Which.Problems.Should().Contain(p => p.Contains("mindestens eine Rolle"));
    }

    [Fact]
    public async Task Mehrere_Rollen_an_einem_Kontakt_sind_der_Normalfall()
    {
        Contact contact = Supplier("Theaterhaus Mitte");
        contact.Roles.Add(new ContactRoleAssignment { Role = ContactRole.Venue });
        contact.Roles.Add(new ContactRoleAssignment { Role = ContactRole.Partner });

        Contact stored = await Service.AddAsync(contact);

        stored.Roles.Should().HaveCount(3);
        stored.HasRole(ContactRole.Venue).Should().BeTrue();
        stored.HasRole(ContactRole.Supplier).Should().BeTrue();
    }

    [Fact]
    public async Task Dieselbe_Rolle_zweimal_weist_die_Datenbank_ab()
    {
        Contact contact = Supplier();
        contact.Roles.Add(new ContactRoleAssignment { Role = ContactRole.Supplier });

        Func<Task> act = () => Service.AddAsync(contact);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Eine_unsinnige_IBAN_wird_abgewiesen()
    {
        Contact contact = Supplier();
        contact.BankDetails = new ContactBankDetails { Iban = "DE12" };

        Func<Task> act = () => Service.AddAsync(contact);

        (await act.Should().ThrowAsync<ContactException>())
            .Which.Problems.Should().Contain(p => p.Contains("IBAN"));
    }

    [Fact]
    public async Task Eine_gueltige_IBAN_geht_durch()
    {
        Contact contact = Supplier();
        contact.BankDetails = new ContactBankDetails
        {
            Iban = "DE02120300000000202051",
            Bic = "BYLADEM1001",
            PaymentTermDays = 30,
        };

        Contact stored = await Service.AddAsync(contact);

        stored.BankDetails!.Iban.Should().Be("DE02120300000000202051");
    }

    [Fact]
    public async Task Presse_braucht_eine_Einwilligung_bevor_geschrieben_wird()
    {
        Contact presse = Person("Jonas", "Kruse", ContactRole.Press);
        Contact stored = await Service.AddAsync(presse);

        stored.MayBeContacted.Should().BeFalse("ohne Einwilligung kein Presseverteiler");

        await Service.UpdateAsync(stored.Id, c => c.ContactConsentAt = DateTimeOffset.UtcNow);

        Context.ChangeTracker.Clear();
        Contact reloaded = await Service.Query().SingleAsync(c => c.Id == stored.Id);
        reloaded.MayBeContacted.Should().BeTrue();
    }

    [Fact]
    public async Task Ein_Lieferant_braucht_keine_Einwilligung()
    {
        Contact stored = await Service.AddAsync(Supplier());

        // Eine Rechnung ist Vertragserfüllung, keine Werbung.
        stored.MayBeContacted.Should().BeTrue();
    }

    [Fact]
    public async Task Ein_Widerruf_beendet_das_Anschreiben()
    {
        Contact presse = Person("Jonas", "Kruse", ContactRole.Press);
        presse.ContactConsentAt = DateTimeOffset.UtcNow;
        Contact stored = await Service.AddAsync(presse);
        stored.MayBeContacted.Should().BeTrue();

        await Service.WithdrawConsentAsync(stored.Id);

        Context.ChangeTracker.Clear();
        Contact reloaded = await Service.Query().SingleAsync(c => c.Id == stored.Id);
        reloaded.MayBeContacted.Should().BeFalse();
    }

    [Fact]
    public async Task Die_Buchhaltung_bucht_gegen_denselben_Kontakt()
    {
        Contact lieferant = await Service.AddAsync(Supplier());

        JournalEntry entry = RentPayment();
        entry.Lines[0].ContactId = lieferant.Id;
        await Journal.AddAsync(entry);

        Context.ChangeTracker.Clear();
        ContactUsage usage = await Service.UsageAsync(lieferant.Id);

        usage.JournalLines.Should().Be(1);
        usage.IsReferenced.Should().BeTrue("deshalb wird er nicht gelöscht, sondern stillgelegt");
    }

    [Fact]
    public async Task Ein_benutzter_Kontakt_wird_stillgelegt_statt_geloescht()
    {
        Contact lieferant = await Service.AddAsync(Supplier());
        JournalEntry entry = RentPayment();
        entry.Lines[0].ContactId = lieferant.Id;
        await Journal.AddAsync(entry);

        await Service.DeactivateAsync(lieferant.Id);

        Context.ChangeTracker.Clear();
        Contact reloaded = await Service.Query().SingleAsync(c => c.Id == lieferant.Id);
        reloaded.IsActive.Should().BeFalse();

        // Die Buchung verweist weiterhin auf ihn -- acht Jahre lang.
        (await Service.UsageAsync(lieferant.Id)).JournalLines.Should().Be(1);
    }

    [Fact]
    public async Task Eine_Person_kann_zu_einer_Organisation_gehoeren()
    {
        Contact haus = await Service.AddAsync(Supplier("Theaterhaus Mitte"));

        Contact person = Person("Anja", "Behrens", ContactRole.Partner);
        person.OrganizationContactId = haus.Id;
        Contact stored = await Service.AddAsync(person);

        stored.OrganizationContactId.Should().Be(haus.Id);
        (await Service.UsageAsync(haus.Id)).People.Should().Be(1);
    }

    [Fact]
    public async Task Eine_Organisation_gehoert_nicht_zu_einer_Organisation()
    {
        Contact haus = await Service.AddAsync(Supplier("Theaterhaus Mitte"));

        Contact andere = Supplier("Tochtergesellschaft");
        andere.OrganizationContactId = haus.Id;

        Func<Task> act = () => Service.AddAsync(andere);

        await act.Should().ThrowAsync<ContactException>();
    }
}
