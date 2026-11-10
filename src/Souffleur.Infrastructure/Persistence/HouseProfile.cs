using System.Text.Json;
using System.Text.Json.Serialization;

namespace Souffleur.Infrastructure.Persistence;

/// <summary>
/// Ein Haus, wie es in einer Vorfuehrinstallation aussehen soll: Name, Anschrift,
/// Spielstaetten und Spielplan.
/// </summary>
/// <remarks>
/// Warum eine Datei und nicht Code: Diese Angaben gehoeren einem bestimmten Haus.
/// Im Repository waeren sie Kundendaten -- derselbe Fehler, der beim ersten
/// souffleur erst nachtraeglich herausgeloest werden musste. Die Datei liegt im
/// Datenverzeichnis der Installation und wird nicht versioniert.
///
/// Was hier *nicht* hineingehoert: der Kontenrahmen, die Kataloge, die
/// Beispielbuchungen. Die sind fuer jedes Theater gleich und bleiben im Code.
/// Hier steht nur, was dieses eine Haus ausmacht.
/// </remarks>
public sealed record HouseProfile
{
    public const string FileName = "haus.json";

    /// <summary>
    /// Bewusst nicht <c>required</c>: Dann wirft der JSON-Leser zuerst, mit
    /// einer Meldung ueber fehlende Eigenschaften eines Typnamens. Wer die
    /// Datei geschrieben hat, soll stattdessen lesen, was ihr fehlt.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    public string? LegalForm { get; init; }

    public string? Street { get; init; }

    public string? PostalCode { get; init; }

    public string? City { get; init; }

    /// <summary>Woher die Angaben stammen. Erscheint nirgends, dient der Nachvollziehbarkeit.</summary>
    public string? Source { get; init; }

    public IReadOnlyList<HouseResource> Resources { get; init; } = [];

    public IReadOnlyList<HouseProduction> Productions { get; init; } = [];

    /// <summary>
    /// Liest das Profil aus dem Datenverzeichnis. Fehlt die Datei, gilt der
    /// eingebaute Beispielbestand.
    /// </summary>
    /// <remarks>
    /// Eine unlesbare Datei wird nicht stillschweigend uebergangen: Wer eine
    /// hinlegt, will sie benutzt sehen, und ein Haus, das sich selbst nicht
    /// wiedererkennt, ist schlimmer als eines, das gar nicht erst startet.
    /// </remarks>
    public static HouseProfile? Load(string dataDirectory) =>
        LoadFile(Path.Combine(dataDirectory, FileName));

    /// <summary>Liest ein Profil aus einer bestimmten Datei -- fuer die Haeuser, die zur Wahl stehen.</summary>
    public static HouseProfile? LoadFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        HouseProfile? profile;

        try
        {
            profile = JsonSerializer.Deserialize<HouseProfile>(File.ReadAllText(path), Options);
        }
        catch (JsonException problem)
        {
            // Die Meldung des Lesers nennt Zeile und Spalte. Davor gehoert der
            // Dateiname, sonst sucht jemand in der falschen Datei.
            throw new InvalidOperationException($"{path}: {problem.Message}", problem);
        }

        if (profile is null)
        {
            throw new InvalidOperationException($"{path} ist leer.");
        }

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new InvalidOperationException($"{path}: Das Haus braucht einen Namen.");
        }

        return profile;
    }

    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>Eine Spielstaette, ein Raum, ein Gewerk.</summary>
public sealed record HouseResource
{
    public required string Name { get; init; }

    /// <summary>Kuerzel, auf das sich die Termine beziehen.</summary>
    public required string ShortName { get; init; }

    /// <summary>Schluessel aus dem Katalog der Ressourcenarten: buehne, probebuehne, werkstatt, raum, technik.</summary>
    public string Kind { get; init; } = "raum";

    public int? Capacity { get; init; }

    /// <summary>Falsch fuer alles, was es mehrfach gibt -- Scheinwerfer etwa.</summary>
    public bool IsExclusive { get; init; } = true;
}

/// <summary>Eine Produktion mit ihren Terminen.</summary>
public sealed record HouseProduction
{
    public required string Title { get; init; }

    /// <summary>Schluessel aus dem Katalog der Terminarten: vorstellung, probe, gastspiel, sonstiges.</summary>
    public string Category { get; init; } = "vorstellung";

    /// <summary>Kuerzel der Spielstaette aus <see cref="HouseProfile.Resources"/>.</summary>
    public string? Resource { get; init; }

    public int DurationMinutes { get; init; } = 120;

    public IReadOnlyList<HousePerformance> Performances { get; init; } = [];
}

/// <summary>Ein einzelner Termin.</summary>
public sealed record HousePerformance
{
    public required DateOnly Date { get; init; }

    /// <summary>Bis einschliesslich -- fuer Installationen, die ueber Wochen laufen.</summary>
    public DateOnly? Until { get; init; }

    public TimeOnly? Time { get; init; }

    /// <summary>Spielort, wenn er vom Haus abweicht: Gastspiele, Tourneen.</summary>
    public string? Location { get; init; }

    /// <summary>„Premiere", „mit Publikumsgespräch", „letzte Vorstellung".</summary>
    public string? Note { get; init; }

    public bool IsCancelled { get; init; }
}
