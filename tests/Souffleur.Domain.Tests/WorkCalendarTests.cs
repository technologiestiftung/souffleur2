using FluentAssertions;
using Souffleur.Domain.Organizations;
using Souffleur.Domain.Personnel;

namespace Souffleur.Domain.Tests;

/// <summary>
/// Was ein Urlaub kostet und wie das Stundenkonto steht -- die Regeln, ueber
/// die am Jahresende gestritten wird.
/// </summary>
public class WorkCalendarTests
{
    private static readonly Guid Anna = Guid.NewGuid();

    private static Employment Vollzeit(DateOnly ab, decimal urlaub = 30) => new()
    {
        ContactId = Anna,
        ValidFrom = ab,
        WeeklyHours = 40,
        WorkDays = WorkWeekdays.MondayToFriday,
        AnnualLeaveDays = urlaub,
    };

    private static Absence Urlaub(DateOnly von, DateOnly bis, AbsenceStatus status = AbsenceStatus.Approved) => new()
    {
        ContactId = Anna,
        Kind = AbsenceKind.Vacation,
        Status = status,
        StartDate = von,
        EndDate = bis,
    };

    private static IEnumerable<DateOnly> Feiertage(int jahr) =>
        PublicHolidays.Of(jahr, FederalState.BE).Select(h => h.Date);

    [Fact]
    public void Ostern_stimmt_fuer_bekannte_Jahre()
    {
        PublicHolidays.EasterSunday(2024).Should().Be(new DateOnly(2024, 3, 31));
        PublicHolidays.EasterSunday(2026).Should().Be(new DateOnly(2026, 4, 5));
        PublicHolidays.EasterSunday(2027).Should().Be(new DateOnly(2027, 3, 28));
    }

    [Fact]
    public void Berlin_hat_den_Frauentag_Bayern_nicht()
    {
        PublicHolidays.Of(2026, FederalState.BE).Should().Contain(h => h.Date == new DateOnly(2026, 3, 8));
        PublicHolidays.Of(2026, FederalState.BY).Should().NotContain(h => h.Date == new DateOnly(2026, 3, 8));
        PublicHolidays.Of(2026, FederalState.BY).Should().Contain(h => h.Name == "Fronleichnam");
    }

    [Fact]
    public void Buss_und_Bettag_ist_der_Mittwoch_vor_dem_23_November()
    {
        PublicHolidays.Of(2026, FederalState.SN)
            .Should().Contain(h => h.Name == "Buß- und Bettag" && h.Date == new DateOnly(2026, 11, 18));
    }

    [Fact]
    public void Eine_Woche_Urlaub_kostet_fuenf_Tage()
    {
        var kalender = new WorkCalendar([Vollzeit(new DateOnly(2026, 1, 1))], Feiertage(2026), []);

        kalender.DaysOf(Urlaub(new DateOnly(2026, 6, 8), new DateOnly(2026, 6, 14))).Should().Be(5);
    }

    [Fact]
    public void Ostern_kostet_keinen_Urlaub_fuer_die_Feiertage()
    {
        var kalender = new WorkCalendar([Vollzeit(new DateOnly(2026, 1, 1))], Feiertage(2026), []);

        // 30.3.–10.4.2026: zehn Werktage, davon Karfreitag und Ostermontag.
        kalender.DaysOf(Urlaub(new DateOnly(2026, 3, 30), new DateOnly(2026, 4, 10))).Should().Be(8);
    }

    [Fact]
    public void Wer_Mittwoch_bis_Sonntag_arbeitet_zahlt_nur_seine_Tage()
    {
        Employment abenddienst = Vollzeit(new DateOnly(2026, 1, 1));
        abenddienst.WorkDays = WorkWeekdays.Wednesday | WorkWeekdays.Thursday | WorkWeekdays.Friday
                               | WorkWeekdays.Saturday | WorkWeekdays.Sunday;

        var kalender = new WorkCalendar([abenddienst], Feiertage(2026), []);

        // Montag bis Freitag: nur Mittwoch, Donnerstag, Freitag sind ihre.
        kalender.DaysOf(Urlaub(new DateOnly(2026, 6, 8), new DateOnly(2026, 6, 12))).Should().Be(3);
    }

    [Fact]
    public void Krank_mit_Bescheinigung_gibt_die_Urlaubstage_zurueck()
    {
        Absence urlaub = Urlaub(new DateOnly(2026, 6, 8), new DateOnly(2026, 6, 12));
        var krank = new Absence
        {
            ContactId = Anna,
            Kind = AbsenceKind.Sick,
            Status = AbsenceStatus.Reported,
            StartDate = new DateOnly(2026, 6, 10),
            EndDate = new DateOnly(2026, 6, 11),
            CertificateReceived = true,
        };

        var kalender = new WorkCalendar([Vollzeit(new DateOnly(2026, 1, 1))], Feiertage(2026), [urlaub, krank]);

        kalender.DaysOf(urlaub).Should().Be(3);
    }

    [Fact]
    public void Krank_ohne_Bescheinigung_bleibt_Urlaub()
    {
        Absence urlaub = Urlaub(new DateOnly(2026, 6, 8), new DateOnly(2026, 6, 12));
        var krank = new Absence
        {
            ContactId = Anna,
            Kind = AbsenceKind.Sick,
            Status = AbsenceStatus.Reported,
            StartDate = new DateOnly(2026, 6, 10),
            EndDate = new DateOnly(2026, 6, 11),
        };

        var kalender = new WorkCalendar([Vollzeit(new DateOnly(2026, 1, 1))], Feiertage(2026), [urlaub, krank]);

        kalender.DaysOf(urlaub).Should().Be(5);
    }

    [Fact]
    public void Wer_im_Juli_anfaengt_hat_den_halben_Anspruch_auf_halbe_Tage_gerundet()
    {
        var kalender = new WorkCalendar([Vollzeit(new DateOnly(2026, 7, 1), urlaub: 30)], [], []);

        // 184 von 365 Tagen: 15,12 -> 15,5.
        kalender.EntitlementOf(2026).Should().Be(15.5m);
    }

    [Fact]
    public void Ein_Wechsel_in_Teilzeit_teilt_den_Anspruch()
    {
        Employment teilzeit = Vollzeit(new DateOnly(2026, 7, 1), urlaub: 18);
        var kalender = new WorkCalendar([Vollzeit(new DateOnly(2020, 1, 1), urlaub: 30), teilzeit], [], []);

        // 181 Tage zu 30, 184 Tage zu 18: 14,88 + 9,07 = 23,95 -> 24.
        kalender.EntitlementOf(2026).Should().Be(24m);
    }

    [Fact]
    public void Das_Urlaubskonto_trennt_Genommenes_und_Beantragtes()
    {
        var kalender = new WorkCalendar(
            [Vollzeit(new DateOnly(2020, 1, 1))],
            Feiertage(2026),
            [
                Urlaub(new DateOnly(2026, 6, 8), new DateOnly(2026, 6, 12)),
                Urlaub(new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 4), AbsenceStatus.Requested),
                Urlaub(new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 7), AbsenceStatus.Rejected),
            ]);

        LeaveBalance konto = kalender.LeaveOf(2026, adjustments: 2);

        konto.Entitlement.Should().Be(30);
        konto.Taken.Should().Be(5);
        konto.Pending.Should().Be(2);
        konto.Remaining.Should().Be(25);
    }

    [Fact]
    public void Urlaub_ueber_Silvester_zaehlt_in_jedem_Jahr_seinen_Teil()
    {
        Absence urlaub = Urlaub(new DateOnly(2026, 12, 28), new DateOnly(2027, 1, 8));
        var kalender = new WorkCalendar(
            [Vollzeit(new DateOnly(2020, 1, 1))],
            [.. Feiertage(2026), .. Feiertage(2027)],
            [urlaub]);

        // 28.–31.12.: vier Tage. 1.1. ist Feiertag, dann 4.–8.1.: fünf.
        kalender.DaysOf(urlaub, 2026).Should().Be(4);
        kalender.DaysOf(urlaub, 2027).Should().Be(5);
    }

    [Fact]
    public void Das_Stundenkonto_schreibt_Urlaub_gut_aber_keinen_Freizeitausgleich()
    {
        var montag = new DateOnly(2026, 6, 8);
        var kalender = new WorkCalendar(
            [Vollzeit(new DateOnly(2020, 1, 1))],
            Feiertage(2026),
            [
                Urlaub(montag, montag),
                new Absence
                {
                    ContactId = Anna,
                    Kind = AbsenceKind.TimeOffInLieu,
                    Status = AbsenceStatus.Approved,
                    StartDate = montag.AddDays(1),
                    EndDate = montag.AddDays(1),
                },
            ]);

        // Mittwoch bis Freitag je neun Stunden.
        Dictionary<DateOnly, int> gearbeitet = new()
        {
            [montag.AddDays(2)] = 540,
            [montag.AddDays(3)] = 540,
            [montag.AddDays(4)] = 540,
        };

        HoursBalance konto = kalender.HoursOf(montag, montag.AddDays(6), gearbeitet);

        konto.Target.Should().Be(5 * 480);
        konto.Credited.Should().Be(480);
        konto.Worked.Should().Be(3 * 540);

        // Drei Stunden Mehrarbeit, ein Tag Freizeitausgleich: minus fuenf.
        konto.Balance.Should().Be(-300);
        konto.MissingDays.Should().BeEmpty();
    }

    [Fact]
    public void Ein_Arbeitstag_ohne_Zeit_und_ohne_Abwesenheit_fehlt()
    {
        var montag = new DateOnly(2026, 6, 8);
        var kalender = new WorkCalendar([Vollzeit(new DateOnly(2020, 1, 1))], Feiertage(2026), []);

        HoursBalance konto = kalender.HoursOf(montag, montag.AddDays(1), new Dictionary<DateOnly, int>
        {
            [montag] = 480,
        });

        konto.MissingDays.Should().Equal(montag.AddDays(1));
    }

    [Fact]
    public void Vor_dem_Vertrag_und_nach_dem_Ende_gibt_es_kein_Soll()
    {
        Employment befristet = Vollzeit(new DateOnly(2026, 6, 10));
        befristet.EndsOn = new DateOnly(2026, 6, 11);
        var kalender = new WorkCalendar([befristet], [], []);

        kalender.HoursOf(new DateOnly(2026, 6, 8), new DateOnly(2026, 6, 12), new Dictionary<DateOnly, int>())
            .Target.Should().Be(2 * 480);
    }

    [Fact]
    public void Ein_halber_Tag_geht_nur_an_einem_Tag()
    {
        Absence zwei = Urlaub(new DateOnly(2026, 6, 8), new DateOnly(2026, 6, 9));
        zwei.IsHalfDay = true;

        zwei.Validate().Should().Contain(p => p.Contains("halber Tag"));
    }
}
