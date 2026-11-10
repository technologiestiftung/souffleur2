using FluentAssertions;
using Souffleur.Infrastructure.Ticketing;

namespace Souffleur.Infrastructure.Tests;

/// <summary>
/// Die Kapazität ist die Bezugsgröße der einzigen Kennzahl, die das
/// Ticketing verspricht. Die Fälle hier bilden ab, wie Häuser ihre
/// Kontingente in pretix tatsächlich schneiden -- übernommen aus dem alten
/// souffleur, wo sie an den Daten eines Hauses entstanden sind.
/// </summary>
public class PretixCapacityTests
{
    private static PretixQuota Quota(int? size, int[] items, int[]? variations = null) =>
        new(size, items, variations ?? []);

    [Fact]
    public void Der_Saalplan_zaehlt_seine_Plaetze()
    {
        PretixCapacity.FromSeats([new(false), new(false), new(false)]).Should().Be(3);
    }

    [Fact]
    public void Gesperrte_Plaetze_zaehlen_nicht_mit()
    {
        // Technikplätze und Sichtbehinderungen sind nicht verkäuflich und
        // gehören nicht in die Bezugsgröße der Auslastung.
        PretixCapacity.FromSeats([new(false), new(true), new(false), new(true)]).Should().Be(2);
    }

    [Fact]
    public void Ohne_Saalplan_greifen_die_Kontingente()
    {
        PretixCapacity.FromSeats([]).Should().BeNull();
    }

    [Fact]
    public void Ein_einzelnes_Kontingent_gibt_seine_Groesse()
    {
        (int? capacity, string? note) = PretixCapacity.FromQuotas([Quota(120, [2])]);

        capacity.Should().Be(120);
        note.Should().BeNull();
    }

    [Fact]
    public void Ueberschneidungsfreie_Kontingente_werden_addiert()
    {
        // Parkett und Rang sind getrennte Produkte -- ihre Plätze summieren sich.
        (int? capacity, _) = PretixCapacity.FromQuotas([Quota(200, [1]), Quota(80, [2])]);

        capacity.Should().Be(280);
    }

    [Fact]
    public void Ein_umfassendes_Kontingent_ist_die_Grenze()
    {
        // „Saalkapazität 200" begrenzt alle Produkte, „Ermäßigt 30" ist ein
        // Teilkontingent darin. 230 wäre falsch -- der Saal hat 200 Plätze.
        (int? capacity, string? note) =
            PretixCapacity.FromQuotas([Quota(200, [1, 2]), Quota(30, [2])]);

        capacity.Should().Be(200);
        note.Should().BeNull();
    }

    [Fact]
    public void Echte_Teilueberlappung_bleibt_unbestimmt()
    {
        // Das eine deckt {1,2}, das andere {2,3} -- keines umfasst das andere.
        (int? capacity, string? note) =
            PretixCapacity.FromQuotas([Quota(100, [1, 2]), Quota(100, [2, 3])]);

        capacity.Should().BeNull();
        note.Should().Contain("überschneiden");
    }

    [Fact]
    public void Ein_unbegrenztes_Kontingent_macht_die_Auslastung_unbestimmbar()
    {
        (int? capacity, string? note) = PretixCapacity.FromQuotas([Quota(null, [1])]);

        capacity.Should().BeNull();
        note.Should().Contain("unbegrenzt");
    }

    [Fact]
    public void Ohne_Kontingent_gibt_es_keine_Zahl()
    {
        (int? capacity, string? note) = PretixCapacity.FromQuotas([]);

        capacity.Should().BeNull();
        note.Should().Contain("kein Kontingent");
    }

    [Fact]
    public void Varianten_sind_etwas_anderes_als_Produkte()
    {
        // Gleiche Produkt-Id, verschiedene Varianten: keine Überschneidung.
        (int? capacity, _) = PretixCapacity.FromQuotas(
            [Quota(50, [], [10]), Quota(70, [], [11])]);

        capacity.Should().Be(120);
    }

    [Fact]
    public void Der_Terminfilter_haengt_sich_an_die_richtige_Stelle()
    {
        PretixClient.Scoped("/quotas/", null).Should().Be("/quotas/");
        PretixClient.Scoped("/quotas/", 7).Should().Be("/quotas/?subevent=7");
        PretixClient.Scoped("/orderpositions/?canceled=false", 7)
            .Should().Be("/orderpositions/?canceled=false&subevent=7");
    }
}
