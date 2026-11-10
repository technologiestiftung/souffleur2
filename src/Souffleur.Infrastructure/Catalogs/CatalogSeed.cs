using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Souffleur.Domain.Catalogs;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Catalogs;

/// <summary>
/// Füllt die Listen einer neuen Installation.
/// </summary>
/// <remarks>
/// Ein leeres Auswahlfeld beim ersten Termin ist eine Hürde, kein
/// Freiheitsgewinn (ADR-0005). Die mitgelieferten Einträge sind deshalb da,
/// aber nicht festgeschrieben: umbenennen, umsortieren, abschalten -- alles
/// erlaubt. Nur löschen nicht, damit eine Installation nicht ohne jede
/// Kategorie dasteht.
///
/// Nachgetragen wird nur, was fehlt. Wer „Probe“ in „Arbeitsprobe“ umbenannt
/// hat, soll sie beim nächsten Start nicht doppelt vorfinden.
/// </remarks>
public static class CatalogSeed
{
    private static readonly (CatalogKind Catalog, string Key, string Label, string? Color)[] Entries =
    [
        // Terminkategorien -- die Farben tragen das Jahresraster. Gewählt so,
        // dass sie sich auch nebeneinander in einer schmalen Spalte noch
        // unterscheiden lassen, hell wie dunkel.
        (CatalogKind.EventCategory, "probe", "Probe", "#7a8b99"),
        (CatalogKind.EventCategory, "hauptprobe", "Hauptprobe", "#5b7c99"),
        (CatalogKind.EventCategory, "generalprobe", "Generalprobe", "#3d6b8c"),
        (CatalogKind.EventCategory, "vorstellung", "Vorstellung", "#6b2c45"),
        (CatalogKind.EventCategory, "premiere", "Premiere", "#a63d5f"),
        (CatalogKind.EventCategory, "gastspiel", "Gastspiel", "#8a5a3c"),
        (CatalogKind.EventCategory, "veranstaltung", "Veranstaltung", "#4a7c59"),
        (CatalogKind.EventCategory, "sitzung", "Sitzung", "#6c6f7d"),
        (CatalogKind.EventCategory, "frist", "Frist", "#b5651d"),
        (CatalogKind.EventCategory, "sperrtermin", "Sperrtermin", "#555a60"),

        // Aufgabenkategorien: wenige und allgemein. Was ein Haus wirklich
        // braucht, weiß es selbst -- hier steht nur, dass die Liste nicht leer
        // ist, wenn das Modul kommt.
        (CatalogKind.TaskCategory, "organisation", "Organisation", "#5b7c99"),
        (CatalogKind.TaskCategory, "technik", "Technik", "#4a7c59"),
        (CatalogKind.TaskCategory, "kommunikation", "Kommunikation", "#a63d5f"),
        (CatalogKind.TaskCategory, "verwaltung", "Verwaltung", "#6c6f7d"),

        // Ressourcenarten: die Räume und Dinge, die ein Termin belegt. Die
        // Ressourcen selbst liefert niemand mit -- welche Bühnen ein Haus hat,
        // weiß nur das Haus.
        (CatalogKind.ResourceKind, "buehne", "Bühne", "#6b2c45"),
        (CatalogKind.ResourceKind, "probebuehne", "Probebühne", "#7a8b99"),
        (CatalogKind.ResourceKind, "werkstatt", "Werkstatt", "#8a5a3c"),
        (CatalogKind.ResourceKind, "raum", "Raum", "#6c6f7d"),
        (CatalogKind.ResourceKind, "fahrzeug", "Fahrzeug", "#4a7c59"),
        (CatalogKind.ResourceKind, "technik", "Technik", "#3d6b8c"),

        // Funktionen am Termin: was jemand an diesem Abend tut. Ohne Farbe --
        // sie erscheint nirgends.
        (CatalogKind.ParticipationRole, "regie", "Regie", null),
        (CatalogKind.ParticipationRole, "ensemble", "Ensemble", null),
        (CatalogKind.ParticipationRole, "inspizienz", "Inspizienz", null),
        (CatalogKind.ParticipationRole, "technik", "Technik", null),
        (CatalogKind.ParticipationRole, "abenddienst", "Abenddienst", null),
        (CatalogKind.ParticipationRole, "einlass", "Einlass", null),

        // Kostenarten, Schlagworte und Produktionen werden nicht mitgeliefert:
        // Sie hängen am Kontenrahmen, an der Arbeitsweise und am Spielplan des
        // Hauses. Ein Vorschlag wäre hier eine Behauptung.
    ];

    public static async Task RunAsync(
        SouffleurDbContext context, ILogger logger, CancellationToken token = default)
    {
        var stored = await context.CatalogEntries
            .Select(e => new { e.Catalog, e.Key })
            .ToListAsync(token);

        HashSet<(CatalogKind, string)> existing = [.. stored.Select(x => (x.Catalog, x.Key))];

        Dictionary<CatalogKind, int> order = [];
        int added = 0;

        foreach ((CatalogKind catalog, string key, string label, string? color) in Entries)
        {
            // Die Reihenfolge zählt je Liste, nicht über alle hinweg.
            int position = order[catalog] = order.GetValueOrDefault(catalog) + 10;

            if (existing.Contains((catalog, key)))
            {
                continue;
            }

            context.CatalogEntries.Add(new CatalogEntry
            {
                Catalog = catalog,
                Key = key,
                Label = label,
                Color = color,
                SortOrder = position,
                IsSystem = true,
            });
            added++;
        }

        if (added > 0)
        {
            await context.SaveChangesAsync(token);
            logger.LogInformation("{Count} Katalogeinträge angelegt.", added);
        }
    }
}
