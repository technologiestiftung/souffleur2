using System.Text;
using FluentAssertions;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Tests;

/// <summary>
/// Das Hausprofil einer Vorfuehrinstallation.
/// </summary>
/// <remarks>
/// Geprueft wird vor allem, was schiefgehen kann, ohne aufzufallen: eine Datei,
/// die stillschweigend ignoriert wird, oder ein Gastspiel, das die eigene
/// Buehne blockiert, obwohl es in einer anderen Stadt stattfindet.
/// </remarks>
public class HouseProfileTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), $"souffleur-profil-{Guid.CreateVersion7():n}");

    public HouseProfileTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private void Write(string json) =>
        File.WriteAllText(Path.Combine(directory, HouseProfile.FileName), json, Encoding.UTF8);

    [Fact]
    public void Ohne_Datei_gilt_der_eingebaute_Bestand()
    {
        HouseProfile.Load(directory).Should().BeNull();
    }

    [Fact]
    public void Ein_Haus_mit_Spielplan_wird_gelesen()
    {
        Write("""
        {
          "name": "Theater am Kanal",
          "city": "Berlin",
          "resources": [
            { "name": "Großer Saal", "shortName": "GS", "kind": "buehne", "capacity": 99 }
          ],
          "productions": [
            {
              "title": "Woyzeck",
              "resource": "GS",
              "durationMinutes": 105,
              "performances": [
                { "date": "2026-12-16", "time": "20:00" },
                { "date": "2026-12-17", "time": "20:00", "note": "letzte Vorstellung" }
              ]
            }
          ]
        }
        """);

        HouseProfile house = HouseProfile.Load(directory)!;

        house.Name.Should().Be("Theater am Kanal");
        house.Resources.Should().ContainSingle().Which.Capacity.Should().Be(99);

        HouseProduction production = house.Productions.Single();
        production.Title.Should().Be("Woyzeck");
        production.Performances.Should().HaveCount(2);
        production.Performances[1].Note.Should().Be("letzte Vorstellung");
        production.Performances[1].Time.Should().Be(new TimeOnly(20, 0));
    }

    [Fact]
    public void Eine_Installation_laeuft_ueber_Tage()
    {
        Write("""
        {
          "name": "Kollektiv Nord",
          "productions": [
            {
              "title": "Lesesaal",
              "performances": [
                { "date": "2026-11-13", "until": "2026-11-27", "location": "Kunstverein, Kassel" }
              ]
            }
          ]
        }
        """);

        HousePerformance performance = HouseProfile.Load(directory)!
            .Productions.Single().Performances.Single();

        performance.Until.Should().Be(new DateOnly(2026, 11, 27));
        performance.Time.Should().BeNull("eine Installation hat keine Anfangszeit");
        performance.Location.Should().Be("Kunstverein, Kassel");
    }

    [Fact]
    public void Eine_abgesagte_Vorstellung_bleibt_im_Spielplan()
    {
        Write("""
        {
          "name": "Haus",
          "productions": [
            {
              "title": "All das Schöne",
              "performances": [
                { "date": "2026-12-03", "time": "20:00", "isCancelled": true }
              ]
            }
          ]
        }
        """);

        HouseProfile.Load(directory)!.Productions.Single()
            .Performances.Single().IsCancelled.Should().BeTrue();
    }

    [Fact]
    public void Ein_Haus_ohne_Namen_wird_abgewiesen()
    {
        Write("""{ "city": "Berlin" }""");

        Action act = () => HouseProfile.Load(directory);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Namen*");
    }

    [Fact]
    public void Eine_kaputte_Datei_faellt_auf_statt_durch()
    {
        // Wer eine Datei hinlegt, will sie benutzt sehen. Ein Haus, das sich
        // selbst nicht wiedererkennt, faellt spaeter auf und kostet mehr.
        Write("{ das ist kein JSON");

        Action act = () => HouseProfile.Load(directory);

        // Mit dem Dateinamen davor -- sonst sucht jemand in der falschen Datei.
        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{HouseProfile.FileName}*");
    }
}
