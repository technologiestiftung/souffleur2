using FluentAssertions;
using Souffleur.Domain.Calendar;
using Souffleur.Domain.Catalogs;
using Souffleur.Infrastructure.Calendar;
using Souffleur.Infrastructure.Catalogs;

namespace Souffleur.Infrastructure.Tests;

/// <summary>
/// Die Listen gehören dem Haus (ADR-0005). Hier steht, was das heißt --
/// und wo die Grenze liegt.
/// </summary>
public class CatalogTests : AccountingTestBase
{
    private CatalogService Service => new(Context);

    private static CatalogEntry Category(string label, string? key = null) => new()
    {
        Catalog = CatalogKind.EventCategory,
        Key = key ?? string.Empty,
        Label = label,
    };

    [Fact]
    public async Task Ein_Eintrag_bekommt_seinen_Schluessel_aus_der_Beschriftung()
    {
        CatalogEntry entry = await Service.AddAsync(Category("Öffentliche Probe"));

        entry.Key.Should().Be("oeffentliche-probe");
    }

    [Fact]
    public async Task Neue_Eintraege_kommen_ans_Ende()
    {
        CatalogEntry first = await Service.AddAsync(Category("Probe"));
        CatalogEntry second = await Service.AddAsync(Category("Vorstellung"));

        second.SortOrder.Should().BeGreaterThan(first.SortOrder);
    }

    [Fact]
    public async Task Derselbe_Schluessel_geht_in_einer_anderen_Liste()
    {
        await Service.AddAsync(Category("Technik"));
        CatalogEntry other = await Service.AddAsync(new CatalogEntry
        {
            Catalog = CatalogKind.TaskCategory,
            Key = string.Empty,
            Label = "Technik",
        });

        other.Key.Should().Be("technik");
    }

    [Fact]
    public async Task Derselbe_Schluessel_geht_nicht_zweimal_in_derselben_Liste()
    {
        await Service.AddAsync(Category("Probe"));

        Func<Task> again = () => Service.AddAsync(Category("Probe"));

        await again.Should().ThrowAsync<CatalogException>().WithMessage("*schon*");
    }

    [Fact]
    public async Task Der_Schluessel_bleibt_beim_Umbenennen()
    {
        // Umbenennen ist der Normalfall; der Schlüssel ist das, worüber Code
        // und Beispieldaten den Eintrag meinen.
        CatalogEntry entry = await Service.AddAsync(Category("Probe"));

        CatalogEntry renamed = await Service.UpdateAsync(entry.Id, e =>
        {
            e.Label = "Arbeitsprobe";
            e.Key = "etwas-anderes";
        });

        renamed.Label.Should().Be("Arbeitsprobe");
        renamed.Key.Should().Be("probe");
    }

    [Fact]
    public async Task Ein_verwendeter_Eintrag_laesst_sich_nicht_loeschen()
    {
        CatalogEntry entry = await Service.AddAsync(Category("Probe"));

        await new CalendarService(Context).AddEventAsync(new CalendarEvent
        {
            Title = "Probe Hamlet",
            CategoryId = entry.Id,
            StartsAt = new DateTimeOffset(2026, 11, 2, 19, 30, 0, TimeSpan.FromHours(1)),
            EndsAt = new DateTimeOffset(2026, 11, 2, 22, 30, 0, TimeSpan.FromHours(1)),
        });

        Func<Task> delete = () => Service.DeleteAsync(entry.Id);

        await delete.Should().ThrowAsync<CatalogException>().WithMessage("*verwendet*");

        // Abschalten geht trotzdem -- und lässt den Termin lesbar.
        await Service.SetActiveAsync(entry.Id, false);
        (await Service.ListAsync(CatalogKind.EventCategory)).Should().BeEmpty();
        (await Service.ListAsync(CatalogKind.EventCategory, includeInactive: true))
            .Should().ContainSingle();
    }

    [Fact]
    public async Task Ein_mitgelieferter_Eintrag_laesst_sich_nicht_loeschen()
    {
        CatalogEntry entry = await Service.AddAsync(new CatalogEntry
        {
            Catalog = CatalogKind.EventCategory,
            Key = "probe",
            Label = "Probe",
            IsSystem = true,
        });

        Func<Task> delete = () => Service.DeleteAsync(entry.Id);

        await delete.Should().ThrowAsync<CatalogException>().WithMessage("*mitgeliefert*");
    }

    [Fact]
    public async Task Ein_unbenutzter_eigener_Eintrag_laesst_sich_loeschen()
    {
        CatalogEntry entry = await Service.AddAsync(Category("Versehen"));

        await Service.DeleteAsync(entry.Id);

        (await Service.ListAsync(CatalogKind.EventCategory, includeInactive: true))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Die_Reihenfolge_bestimmt_das_Haus()
    {
        CatalogEntry probe = await Service.AddAsync(Category("Probe"));
        CatalogEntry general = await Service.AddAsync(Category("Generalprobe"));

        await Service.ReorderAsync(CatalogKind.EventCategory, [general.Id, probe.Id]);

        List<CatalogEntry> ordered = await Service.ListAsync(CatalogKind.EventCategory);
        ordered.Select(e => e.Label).Should().Equal("Generalprobe", "Probe");
    }

    [Fact]
    public async Task Eine_Farbe_muss_eine_Farbe_sein()
    {
        CatalogEntry entry = Category("Probe");
        entry.Color = "dunkelrot";

        Func<Task> add = () => Service.AddAsync(entry);

        await add.Should().ThrowAsync<CatalogException>().WithMessage("*#rrggbb*");
    }

    [Fact]
    public async Task Nur_Listen_mit_Gliederung_kennen_eine_Ebene_darunter()
    {
        CatalogEntry parent = await Service.AddAsync(Category("Probe"));

        CatalogEntry child = Category("Sitzprobe");
        child.ParentId = parent.Id;

        Func<Task> add = () => Service.AddAsync(child);

        await add.Should().ThrowAsync<CatalogException>().WithMessage("*Untergliederung*");
    }

    [Fact]
    public async Task Kostenarten_duerfen_sich_gliedern()
    {
        CatalogEntry parent = await Service.AddAsync(new CatalogEntry
        {
            Catalog = CatalogKind.CostType,
            Key = string.Empty,
            Label = "Produktionskosten",
        });

        CatalogEntry child = await Service.AddAsync(new CatalogEntry
        {
            Catalog = CatalogKind.CostType,
            Key = string.Empty,
            Label = "Bühnenbild",
            ParentId = parent.Id,
        });

        child.ParentId.Should().Be(parent.Id);

        // Der übergeordnete Eintrag lässt sich nicht löschen, solange etwas
        // darunter hängt -- sonst verlöre das Kind seine Einordnung.
        Func<Task> delete = () => Service.DeleteAsync(parent.Id);
        await delete.Should().ThrowAsync<CatalogException>().WithMessage("*verwendet*");
    }
}
