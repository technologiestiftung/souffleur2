using FluentAssertions;
using Souffleur.Domain.Calendar;
using Souffleur.Domain.Contacts;
using Souffleur.Domain.Personnel;
using Souffleur.Infrastructure.Calendar;
using Souffleur.Infrastructure.Personnel;

namespace Souffleur.Infrastructure.Tests;

/// <summary>
/// Beantragen, entscheiden, krank melden -- und wo eine Abwesenheit sonst
/// auftaucht: im Kalender ohne Grund, beim Einteilen als Hinweis.
/// </summary>
public class AbsenceTests : AccountingTestBase
{
    /// <summary>Ein Mittwoch im Juni, damit Wochenenden und Feiertage nicht stoeren.</summary>
    private static readonly DateTimeOffset Jetzt = new(2026, 6, 17, 10, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Montag = new(2026, 6, 22);

    private AbsenceService Service => new(Context, new Uhr(Jetzt));

    private async Task<Contact> PersonAsync(string surname = "Krause")
    {
        var contact = new Contact
        {
            Kind = ContactKind.Person,
            Name = $"{surname}, Anna",
            GivenName = "Anna",
            Surname = surname,
            Roles = [new ContactRoleAssignment { Role = ContactRole.Staff }],
        };

        Context.Contacts.Add(contact);
        await Context.SaveChangesAsync();
        return contact;
    }

    private Task<Absence> UrlaubAsync(Contact person, DateOnly von, DateOnly bis, string? notiz = null) =>
        Service.RequestAsync(new AbsenceInput(person.Id, AbsenceKind.Vacation, von, bis, Note: notiz));

    [Fact]
    public async Task Urlaub_wird_beantragt_und_wartet()
    {
        Contact anna = await PersonAsync();

        Absence urlaub = await UrlaubAsync(anna, Montag, Montag.AddDays(4), "Ostsee");

        urlaub.Status.Should().Be(AbsenceStatus.Requested);
        urlaub.Note.Should().Be("Ostsee");
        (await Service.OpenRequestsAsync()).Should().ContainSingle(a => a.Id == urlaub.Id);
    }

    [Fact]
    public async Task Krankheit_gilt_sofort_und_vergisst_jede_Notiz()
    {
        Contact anna = await PersonAsync();

        Absence krank = await Service.RequestAsync(new AbsenceInput(
            anna.Id, AbsenceKind.Sick, Montag, Montag.AddDays(1), Note: "Migräne"));

        krank.Status.Should().Be(AbsenceStatus.Reported);
        krank.Note.Should().BeNull();
    }

    [Fact]
    public async Task Zwei_Urlaube_zur_selben_Zeit_gehen_nicht()
    {
        Contact anna = await PersonAsync();
        await UrlaubAsync(anna, Montag, Montag.AddDays(4));

        Func<Task> nochmal = () => UrlaubAsync(anna, Montag.AddDays(2), Montag.AddDays(6));

        await nochmal.Should().ThrowAsync<AbsenceException>().WithMessage("*schon Urlaub*");
    }

    [Fact]
    public async Task Krank_im_Urlaub_ist_erlaubt()
    {
        Contact anna = await PersonAsync();
        Absence urlaub = await UrlaubAsync(anna, Montag, Montag.AddDays(4));
        await Service.DecideAsync(urlaub.Id, approve: true, note: null, deciderUserId: null);

        Absence krank = await Service.RequestAsync(new AbsenceInput(
            anna.Id, AbsenceKind.Sick, Montag.AddDays(1), Montag.AddDays(2), CertificateReceived: true));

        krank.Status.Should().Be(AbsenceStatus.Reported);
        (await Service.DaysOfAsync([urlaub]))[urlaub.Id].Should().Be(3);
    }

    [Fact]
    public async Task Ablehnen_ohne_Grund_geht_nicht()
    {
        Contact anna = await PersonAsync();
        Absence urlaub = await UrlaubAsync(anna, Montag, Montag);

        Func<Task> ablehnen = () => Service.DecideAsync(urlaub.Id, approve: false, note: " ", deciderUserId: null);

        await ablehnen.Should().ThrowAsync<AbsenceException>().WithMessage("*Grund*");
    }

    [Fact]
    public async Task Entschiedenes_laesst_sich_nicht_noch_einmal_entscheiden()
    {
        Contact anna = await PersonAsync();
        Absence urlaub = await UrlaubAsync(anna, Montag, Montag);
        await Service.DecideAsync(urlaub.Id, approve: false, note: "Premiere", deciderUserId: null);

        Func<Task> doch = () => Service.DecideAsync(urlaub.Id, approve: true, note: null, deciderUserId: null);

        await doch.Should().ThrowAsync<AbsenceException>().WithMessage("*abgelehnt*");
    }

    [Fact]
    public async Task Genehmigtes_laesst_sich_nur_mit_Grund_aufheben()
    {
        Contact anna = await PersonAsync();
        Absence urlaub = await UrlaubAsync(anna, Montag, Montag);
        await Service.DecideAsync(urlaub.Id, approve: true, note: null, deciderUserId: null);

        Func<Task> ohne = () => Service.WithdrawAsync(urlaub.Id, null, null);
        await ohne.Should().ThrowAsync<AbsenceException>().WithMessage("*Grund*");

        (Absence weg, AbsenceStatus vorher) = await Service.WithdrawAsync(urlaub.Id, "Gastspiel verschoben", null);
        weg.Status.Should().Be(AbsenceStatus.Withdrawn);
        vorher.Should().Be(AbsenceStatus.Approved);
    }

    [Fact]
    public async Task Ein_Zurueckgezogener_Antrag_gibt_den_Platz_frei()
    {
        Contact anna = await PersonAsync();
        Absence erster = await UrlaubAsync(anna, Montag, Montag);
        await Service.WithdrawAsync(erster.Id, null, null);

        Absence zweiter = await UrlaubAsync(anna, Montag, Montag);

        zweiter.Status.Should().Be(AbsenceStatus.Requested);
    }

    [Fact]
    public async Task Das_Konto_rechnet_mit_Vertrag_Gutschrift_und_Feiertagen()
    {
        Contact anna = await PersonAsync();
        await Service.SaveEmploymentAsync(null, new Employment
        {
            ContactId = anna.Id,
            ValidFrom = new DateOnly(2020, 1, 1),
            WeeklyHours = 40,
            AnnualLeaveDays = 30,
        });
        await Service.AddAdjustmentAsync(anna.Id, 2026, 2, "Rest aus 2025");

        // Pfingstmontag 2026 ist der 25. Mai: Die Woche kostet vier Tage.
        Absence pfingsten = await UrlaubAsync(anna, new DateOnly(2026, 5, 25), new DateOnly(2026, 5, 29));
        await Service.DecideAsync(pfingsten.Id, approve: true, note: null, deciderUserId: null);

        PersonBalance konto = await Service.BalanceAsync(anna.Id, 2026);

        konto.Leave.Entitlement.Should().Be(30);
        konto.Leave.Adjustments.Should().Be(2);
        konto.Leave.Taken.Should().Be(4);
        konto.Leave.Remaining.Should().Be(28);

        // Bis gestern gerechnet, also bis zum 16. Juni. Gestempelt ist nichts,
        // also steht das Soll ohne die Urlaubstage im Minus.
        konto.Hours.Should().NotBeNull();
        konto.Hours!.To.Should().Be(new DateOnly(2026, 6, 16));
        konto.Hours.Credited.Should().Be(4 * 480);
        konto.Hours.Balance.Should().Be(konto.Hours.Credited - konto.Hours.Target);
    }

    [Fact]
    public async Task Zwei_Vertragsstaende_am_selben_Tag_gehen_nicht()
    {
        Contact anna = await PersonAsync();
        var ab = new DateOnly(2026, 1, 1);
        await Service.SaveEmploymentAsync(null, new Employment { ContactId = anna.Id, ValidFrom = ab, WeeklyHours = 40 });

        Func<Task> zweiter = () => Service.SaveEmploymentAsync(
            null, new Employment { ContactId = anna.Id, ValidFrom = ab, WeeklyHours = 20 });

        await zweiter.Should().ThrowAsync<AbsenceException>().WithMessage("*schon einen Vertragsstand*");
    }

    [Fact]
    public async Task Freie_Tage_des_Hauses_kommen_zu_den_Feiertagen()
    {
        await Service.AddHouseHolidayAsync(12, 24, "Heiligabend");

        List<Holiday> tage = await Service.HolidaysAsync(2026);

        tage.Should().Contain(h => h.Date == new DateOnly(2026, 12, 24) && h.IsHouseDay);
        tage.Should().Contain(h => h.Date == new DateOnly(2026, 3, 8) && !h.IsHouseDay);
    }

    [Fact]
    public async Task Im_Kalender_steht_nur_dass_jemand_fehlt()
    {
        Contact anna = await PersonAsync();
        await Service.RequestAsync(new AbsenceInput(anna.Id, AbsenceKind.Sick, Montag, Montag.AddDays(1)));

        List<CalendarOccurrence> tag = await new CalendarService(Context).RangeAsync(Montag, Montag);

        CalendarOccurrence eintrag = tag.Should().ContainSingle(o => o.Source == CalendarSource.Absence).Subject;
        eintrag.Title.Should().Be("Abwesend: Krause, Anna");
        eintrag.Title.Should().NotContain("Krank");
        eintrag.Description.Should().BeNull();
    }

    [Fact]
    public async Task Ein_offener_Antrag_steht_nicht_im_Kalender()
    {
        Contact anna = await PersonAsync();
        await UrlaubAsync(anna, Montag, Montag);

        List<CalendarOccurrence> tag = await new CalendarService(Context).RangeAsync(Montag, Montag);

        tag.Should().NotContain(o => o.Source == CalendarSource.Absence);
    }

    [Fact]
    public async Task Wer_abwesend_ist_wird_beim_Einteilen_gemeldet()
    {
        Contact anna = await PersonAsync();
        Absence urlaub = await UrlaubAsync(anna, Montag, Montag);
        await Service.DecideAsync(urlaub.Id, approve: true, note: null, deciderUserId: null);

        var calendar = new CalendarService(Context);
        Func<Task> einteilen = () => calendar.AddEventAsync(
            new CalendarEvent
            {
                Title = "Vorstellung",
                StartsAt = EventSeries.ToInstant(Montag, new TimeOnly(19, 30), TimeZoneInfo.Utc),
                EndsAt = EventSeries.ToInstant(Montag, new TimeOnly(22, 0), TimeZoneInfo.Utc),
            },
            participants: [new ParticipantInput(anna.Id)]);

        ScheduleConflict konflikt = (await einteilen.Should().ThrowAsync<ScheduleConflictException>())
            .Which.Conflicts.Should().ContainSingle().Subject;

        konflikt.Title.Should().Be("Abwesend");
        konflikt.SubjectName.Should().Be("Krause, Anna");
    }

    [Fact]
    public async Task Die_Lohnbuchhaltung_hoert_nur_von_dem_was_sie_angeht()
    {
        Contact anna = await PersonAsync();
        Absence krank = await Service.RequestAsync(new AbsenceInput(anna.Id, AbsenceKind.Sick, Montag, Montag));
        Absence urlaub = await UrlaubAsync(anna, Montag.AddDays(7), Montag.AddDays(7));

        (await Service.UnreportedToPayrollAsync()).Should().ContainSingle(a => a.Id == krank.Id);

        await Service.MarkPayrollAsync(krank.Id, reported: true);
        (await Service.UnreportedToPayrollAsync()).Should().BeEmpty();

        Func<Task> urlaubMelden = () => Service.MarkPayrollAsync(urlaub.Id, reported: true);
        await urlaubMelden.Should().ThrowAsync<AbsenceException>();
    }

    private sealed class Uhr(DateTimeOffset jetzt) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => jetzt;
    }
}
