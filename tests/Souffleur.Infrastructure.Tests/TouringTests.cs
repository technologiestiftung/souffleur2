using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Accounting;
using Souffleur.Domain.Calendar;
using Souffleur.Domain.Catalogs;
using Souffleur.Domain.Contacts;
using Souffleur.Domain.Modules;
using Souffleur.Domain.Productions;
using Souffleur.Domain.Touring;
using Souffleur.Infrastructure.Calendar;
using Souffleur.Infrastructure.Touring;

namespace Souffleur.Infrastructure.Tests;

/// <summary>
/// Tourneen und Gastspiele -- und die Stellen, an denen sie die anderen
/// Module berühren: Spielstätte aus den Kontakten, Vorstellungen im Kalender,
/// Reisende als Beteiligte, Gage am Beleg.
/// </summary>
public class TouringTests : AccountingTestBase
{
    private TouringService Service => new(Context);

    private CalendarService Calendar => new(Context);

    private async Task<Contact> VenueAsync(string name = "Kampnagel", string city = "Hamburg", bool withRole = true)
    {
        var contact = new Contact
        {
            Kind = ContactKind.Organization,
            Name = name,
            City = city,
            Roles = withRole ? [new ContactRoleAssignment { Role = ContactRole.Venue }] : [new ContactRoleAssignment { Role = ContactRole.Supplier }],
        };
        Context.Contacts.Add(contact);
        await Context.SaveChangesAsync();
        return contact;
    }

    private async Task<Contact> PersonAsync(string surname)
    {
        var contact = new Contact
        {
            Kind = ContactKind.Person,
            Name = $"{surname}, Lea",
            GivenName = "Lea",
            Surname = surname,
            Roles = [new ContactRoleAssignment { Role = ContactRole.Artist }],
        };
        Context.Contacts.Add(contact);
        await Context.SaveChangesAsync();
        return contact;
    }

    private async Task<Production> ProductionAsync(string title = "Oratorium")
    {
        var production = new Production { Title = title, Status = ProductionStatus.Running };
        Context.Productions.Add(production);
        await Context.SaveChangesAsync();
        return production;
    }

    private async Task EnableTouringAsync(bool on = true)
    {
        ModuleSetting? setting = await Context.ModuleSettings.FirstOrDefaultAsync(m => m.Module == SouffleurModule.Touring);
        if (setting is null)
        {
            setting = new ModuleSetting { Module = SouffleurModule.Touring, IsEnabled = on };
            Context.ModuleSettings.Add(setting);
        }
        else
        {
            setting.IsEnabled = on;
        }

        await Context.SaveChangesAsync();
    }

    private static readonly DateTimeOffset Abend = new(2027, 3, 12, 19, 0, 0, TimeSpan.Zero);

    private async Task<(Tour Tour, TourStop Stop)> TourneeAsync(
        TourStopStatus status = TourStopStatus.Confirmed, params Guid[] members)
    {
        Contact venue = await VenueAsync();
        Tour tour = await Service.AddAsync(
            new Tour { Title = "Herbsttournee" },
            [.. members.Select(id => new MemberInput(id))]);

        TourStop stop = await Service.AddStopAsync(tour.Id, new TourStop
        {
            VenueId = venue.Id,
            Status = status,
            ArrivalDate = new DateOnly(2027, 3, 11),
            DepartureDate = new DateOnly(2027, 3, 14),
            Fee = 4200m,
        }, [new PerformanceInput(Abend, Abend.AddHours(2)), new PerformanceInput(Abend.AddDays(1), Abend.AddDays(1).AddHours(2))]);

        return (tour, stop);
    }

    // --- Die Tournee und ihre Gastspiele ---

    [Fact]
    public async Task Eine_Tournee_bekommt_Gastspiele_und_spannt_sich_ueber_sie()
    {
        (Tour tour, _) = await TourneeAsync();

        Tour? found = await Service.FindAsync(tour.Id);

        found!.Stops.Should().ContainSingle()
            .Which.Performances.Should().HaveCount(2);
        found.Span.Should().Be((new DateOnly(2027, 3, 11), new DateOnly(2027, 3, 14)));
        found.Stops[0].DisplayTitle(found.Title).Should().Be("Herbsttournee · Kampnagel");
        found.Stops[0].Location.Should().Be("Kampnagel, Hamburg");
    }

    [Fact]
    public async Task Die_Spielstaette_muss_im_Adressbuch_die_Rolle_Spielstaette_haben()
    {
        Contact druckerei = await VenueAsync("Druckerei Klein", "Berlin", withRole: false);
        Tour tour = await Service.AddAsync(new Tour { Title = "Tournee" });

        Func<Task> act = () => Service.AddStopAsync(tour.Id, new TourStop { VenueId = druckerei.Id });

        (await act.Should().ThrowAsync<TouringException>())
            .Which.Problems.Should().ContainMatch("*Rolle Spielstätte*");
    }

    [Fact]
    public async Task Die_Produktion_kommt_aus_der_Liste_der_Produktionen()
    {
        Production production = await ProductionAsync();
        var fremd = new CatalogEntry { Catalog = CatalogKind.TaskCategory, Key = "x", Label = "Fremd" };
        Context.CatalogEntries.Add(fremd);
        await Context.SaveChangesAsync();

        Tour tour = await Service.AddAsync(new Tour { Title = "Tournee", ProductionId = production.Id });
        (await Service.FindAsync(tour.Id))!.Production!.Title.Should().Be("Oratorium");

        Func<Task> act = () => Service.AddAsync(new Tour { Title = "Falsch", ProductionId = fremd.Id });
        await act.Should().ThrowAsync<TouringException>();
    }

    [Fact]
    public async Task Abreise_vor_Anreise_und_Vorstellung_ausserhalb_der_Reise_werden_abgewiesen()
    {
        Contact venue = await VenueAsync();
        Tour tour = await Service.AddAsync(new Tour { Title = "Tournee" });

        Func<Task> rueckwaerts = () => Service.AddStopAsync(tour.Id, new TourStop
        {
            VenueId = venue.Id,
            ArrivalDate = new DateOnly(2027, 3, 14),
            DepartureDate = new DateOnly(2027, 3, 11),
        });
        (await rueckwaerts.Should().ThrowAsync<TouringException>())
            .Which.Problems.Should().Contain("Die Abreise liegt vor der Anreise.");

        Func<Task> daneben = () => Service.AddStopAsync(tour.Id, new TourStop
        {
            VenueId = venue.Id,
            ArrivalDate = new DateOnly(2027, 3, 11),
            DepartureDate = new DateOnly(2027, 3, 14),
        }, [new PerformanceInput(Abend.AddDays(10), Abend.AddDays(10).AddHours(2))]);
        (await daneben.Should().ThrowAsync<TouringException>())
            .Which.Problems.Should().ContainMatch("*nach der Abreise*");
    }

    [Fact]
    public async Task Abgerechnetes_laesst_sich_nicht_loeschen()
    {
        (Tour tour, TourStop stop) = await TourneeAsync(TourStopStatus.Settled);

        Func<Task> stopWeg = () => Service.DeleteStopAsync(stop.Id);
        await stopWeg.Should().ThrowAsync<TouringException>();

        Func<Task> tourWeg = () => Service.DeleteAsync(tour.Id);
        await tourWeg.Should().ThrowAsync<TouringException>();

        (await Context.Tours.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Ein_Gastspiel_ohne_Beleg_verschwindet_mit_seiner_Tournee()
    {
        (Tour tour, _) = await TourneeAsync(TourStopStatus.Inquiry);

        await Service.DeleteAsync(tour.Id);

        (await Context.TourStops.CountAsync()).Should().Be(0);
        (await Context.TourPerformances.CountAsync()).Should().Be(0);
    }

    // --- Finanzen: die Gage haengt am Beleg ---

    [Fact]
    public async Task Die_Gage_haengt_nur_an_einem_Ausgangsbeleg()
    {
        (_, TourStop stop) = await TourneeAsync();
        // Belegnummern vergibt sonst der Belegdienst -- hier von Hand, weil
        // nur der Verweis geprüft wird, nicht die Nummernfolge.
        var eingang = new Voucher
        {
            Number = 1,
            FiscalYear = 2027, Date = new DateOnly(2027, 3, 20), GrossAmount = 4200m,
            Description = "Hotel", Direction = VoucherDirection.Incoming, Source = VoucherSource.Upload,
        };
        var ausgang = new Voucher
        {
            Number = 2,
            FiscalYear = 2027, Date = new DateOnly(2027, 3, 20), GrossAmount = 4200m,
            Description = "Gage Kampnagel", Direction = VoucherDirection.Outgoing, Source = VoucherSource.Generated,
        };
        Context.Vouchers.AddRange(eingang, ausgang);
        await Context.SaveChangesAsync();

        Func<Task> falsch = () => Service.UpdateStopAsync(stop.Id, s => s.VoucherId = eingang.Id);
        (await falsch.Should().ThrowAsync<TouringException>())
            .Which.Problems.Should().ContainMatch("*Eingangsrechnung*");

        await Service.UpdateStopAsync(stop.Id, s => s.VoucherId = ausgang.Id);
        (await Service.FindStopAsync(stop.Id))!.Voucher!.Description.Should().Be("Gage Kampnagel");
    }

    // --- Kalender und Personal ---

    [Fact]
    public async Task Vorstellungen_und_Reise_erscheinen_im_Kalender_mit_den_Reisenden()
    {
        await EnableTouringAsync();
        Contact lea = await PersonAsync("Berg");
        await TourneeAsync(TourStopStatus.Confirmed, lea.Id);

        List<CalendarOccurrence> entries = await Calendar.RangeAsync(
            new DateOnly(2027, 3, 1), new DateOnly(2027, 3, 31));

        List<CalendarOccurrence> touring = [.. entries.Where(o => o.Source == CalendarSource.Touring)];

        // Ein Balken für die Reise, zwei Vorstellungen.
        touring.Should().HaveCount(3);
        touring.Where(o => o.IsAllDay).Should().ContainSingle()
            .Which.Should().Match<CalendarOccurrence>(o =>
                o.Title == "Gastspiel: Herbsttournee · Kampnagel"
                && o.LocalDate == new DateOnly(2027, 3, 11)
                && o.LocalEndDate == new DateOnly(2027, 3, 14)
                && o.Location == "Kampnagel, Hamburg");
        touring.Where(o => !o.IsAllDay).Should().HaveCount(2)
            .And.AllSatisfy(o =>
            {
                o.Status.Should().Be(EventStatus.Confirmed);
                o.CategoryLabel.Should().BeNull("ohne Katalog-Seed gibt es keine Kategorie Gastspiel");
                o.Participants.Should().ContainSingle().Which.Name.Should().Be("Berg, Lea");
            });

        // Der Personenfilter greift auch auf Gastspiele -- „was habe ich im März?"
        List<CalendarOccurrence> nurLea = await Calendar.RangeAsync(
            new DateOnly(2027, 3, 1), new DateOnly(2027, 3, 31), people: [lea.Id]);
        nurLea.Should().HaveCount(3);
    }

    [Fact]
    public async Task Eine_Anfrage_steht_im_Kalender_als_Vorbehalt_eine_Absage_gar_nicht()
    {
        await EnableTouringAsync();
        (_, TourStop stop) = await TourneeAsync(TourStopStatus.Inquiry);

        List<CalendarOccurrence> entries = await Calendar.RangeAsync(
            new DateOnly(2027, 3, 1), new DateOnly(2027, 3, 31));
        entries.Where(o => o.Source == CalendarSource.Touring)
            .Should().HaveCount(3).And.AllSatisfy(o => o.Status.Should().Be(EventStatus.Planned));

        await Service.UpdateStopAsync(stop.Id, s => s.Status = TourStopStatus.Cancelled);

        entries = await Calendar.RangeAsync(new DateOnly(2027, 3, 1), new DateOnly(2027, 3, 31));
        entries.Should().NotContain(o => o.Source == CalendarSource.Touring);
    }

    [Fact]
    public async Task Ist_das_Modul_aus_zeigt_der_Kalender_keine_Gastspiele()
    {
        await TourneeAsync();

        // Touring ist in einer frischen Installation aus.
        List<CalendarOccurrence> entries = await Calendar.RangeAsync(
            new DateOnly(2027, 3, 1), new DateOnly(2027, 3, 31));
        entries.Should().NotContain(o => o.Source == CalendarSource.Touring);

        await EnableTouringAsync();
        entries = await Calendar.RangeAsync(new DateOnly(2027, 3, 1), new DateOnly(2027, 3, 31));
        entries.Should().Contain(o => o.Source == CalendarSource.Touring);
    }

    [Fact]
    public async Task Die_Reisenden_werden_als_Ganzes_ersetzt_und_muessen_im_Adressbuch_stehen()
    {
        Contact lea = await PersonAsync("Berg");
        Contact tom = await PersonAsync("Adler");
        (Tour tour, _) = await TourneeAsync(TourStopStatus.Confirmed, lea.Id);

        await Service.UpdateAsync(tour.Id, _ => { }, [new MemberInput(tom.Id)]);
        (await Service.FindAsync(tour.Id))!.Members.Should().ContainSingle()
            .Which.Contact!.Surname.Should().Be("Adler");

        Func<Task> unbekannt = () => Service.UpdateAsync(tour.Id, _ => { }, [new MemberInput(Guid.NewGuid())]);
        await unbekannt.Should().ThrowAsync<TouringException>();
    }
}
