using FluentAssertions;
using Souffleur.Domain.Personnel;

namespace Souffleur.Domain.Tests;

/// <summary>
/// Was ein Zeiteintrag selbst ueber sich weiss -- ohne Datenbank.
/// </summary>
public class TimeEntryTests
{
    private static readonly DateTimeOffset Morning =
        new(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(1));

    private static TimeEntry Entry(int hours, int breakMinutes = 0) => new()
    {
        ContactId = Guid.CreateVersion7(),
        StartedAt = Morning,
        EndedAt = Morning.AddHours(hours),
        BreakMinutes = breakMinutes,
        Source = TimeEntrySource.Clock,
    };

    [Fact]
    public void Die_Pause_geht_von_der_Spanne_ab()
    {
        Entry(8, breakMinutes: 30).WorkedMinutes.Should().Be(450);
    }

    [Fact]
    public void Eine_laufende_Zeit_hat_keine_Dauer()
    {
        var running = new TimeEntry
        {
            ContactId = Guid.CreateVersion7(),
            StartedAt = Morning,
            Source = TimeEntrySource.Clock,
        };

        running.IsRunning.Should().BeTrue();
        running.WorkedMinutes.Should().BeNull();
    }

    [Fact]
    public void Ein_Ende_vor_dem_Beginn_wird_abgewiesen()
    {
        TimeEntry entry = Entry(8);
        entry.EndedAt = Morning.AddHours(-1);

        entry.Validate().Should().ContainSingle()
            .Which.Should().Contain("vor dem Beginn");
    }

    [Fact]
    public void Eine_Spanne_ueber_24_Stunden_ist_ein_vergessenes_Gehen()
    {
        Entry(25).Validate().Should().ContainSingle()
            .Which.Should().Contain("24 Stunden");
    }

    [Fact]
    public void Eine_Pause_so_lang_wie_die_Spanne_wird_abgewiesen()
    {
        Entry(2, breakMinutes: 120).Validate().Should().ContainSingle()
            .Which.Should().Contain("Pause");
    }

    [Fact]
    public void Acht_Stunden_mit_halber_Stunde_Pause_geben_keinen_Hinweis()
    {
        Entry(8, breakMinutes: 30).Notices().Should().BeEmpty();
    }

    [Fact]
    public void Ueber_sechs_Stunden_ohne_Pause_wird_angemerkt()
    {
        Entry(7).Notices().Should().ContainSingle()
            .Which.Should().Contain("§ 4 ArbZG");
    }

    [Fact]
    public void Ueber_zehn_Stunden_wird_angemerkt_aber_nicht_abgewiesen()
    {
        TimeEntry entry = Entry(11, breakMinutes: 45);

        entry.Validate().Should().BeEmpty();
        entry.Notices().Should().ContainSingle()
            .Which.Should().Contain("§ 3 ArbZG");
    }

    [Fact]
    public void Minuten_erscheinen_als_Stundenangabe()
    {
        TimeEntry.Format(450).Should().Be("7:30 Stunden");
    }
}
