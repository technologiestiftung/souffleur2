using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Identity;
using Souffleur.Infrastructure.Identity;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Tests;

/// <summary>
/// Was jemand fuer sich einstellt, gehoert ihm allein -- und darf nie etwas
/// kaputtmachen, wenn es fehlt.
/// </summary>
public class UserPreferenceTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly Guid anna = Guid.CreateVersion7();
    private readonly Guid ben = Guid.CreateVersion7();

    private SouffleurDbContext context = null!;

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        context = new SouffleurDbContext(
            new DbContextOptionsBuilder<SouffleurDbContext>().UseSqlite(connection).Options,
            TimeProvider.System,
            new FixedUser(anna));
        await context.Database.EnsureCreatedAsync();

        // Die Einstellungen haengen am Konto. Ohne die beiden Konten greift
        // der Fremdschluessel -- was richtig ist: Eine Einstellung ohne
        // Person soll es nicht geben.
        context.Users.AddRange(
            new User { Id = anna, GivenName = "Anna", Surname = "Krause", UserName = "anna" },
            new User { Id = ben, GivenName = "Ben", Surname = "Schröder", UserName = "ben" });
        await context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await context.DisposeAsync();
        await connection.DisposeAsync();
    }

    private UserPreferenceService Fuer(Guid userId) =>
        new(context, new FixedUser(userId));

    [Fact]
    public async Task Was_gesetzt_wurde_kommt_zurueck()
    {
        Assert.True(await Fuer(anna).SetAsync("module", "kalender\nbelege"));

        IReadOnlyDictionary<string, string> meine = await Fuer(anna).MineAsync();

        Assert.Equal("kalender\nbelege", meine["module"]);
    }

    [Fact]
    public async Task Derselbe_Schluessel_wird_ueberschrieben_und_nicht_verdoppelt()
    {
        await Fuer(anna).SetAsync("module", "kalender");
        await Fuer(anna).SetAsync("module", "belege");

        Assert.Equal("belege", (await Fuer(anna).MineAsync())["module"]);
        Assert.Equal(1, await context.UserPreferences.CountAsync());
    }

    [Fact]
    public async Task Jeder_sieht_nur_das_Eigene()
    {
        await Fuer(anna).SetAsync("module", "kalender");
        await Fuer(ben).SetAsync("module", "belege");

        Assert.Equal("kalender", (await Fuer(anna).MineAsync())["module"]);
        Assert.Equal("belege", (await Fuer(ben).MineAsync())["module"]);
    }

    [Fact]
    public async Task Zuruecksetzen_loescht_nur_den_einen_Schluessel()
    {
        await Fuer(anna).SetAsync("module", "kalender");
        await Fuer(anna).SetAsync("aktionen:Belege", "anlegen");

        await Fuer(anna).RemoveAsync("module");

        IReadOnlyDictionary<string, string> meine = await Fuer(anna).MineAsync();
        Assert.False(meine.ContainsKey("module"));
        Assert.True(meine.ContainsKey("aktionen:Belege"));
    }

    [Fact]
    public async Task Ohne_Anmeldung_passiert_nichts()
    {
        var ohne = new UserPreferenceService(context, new FixedUser(null));

        Assert.False(await ohne.SetAsync("module", "kalender"));
        Assert.Empty(await ohne.MineAsync());
    }

    [Fact]
    public async Task Ueber_der_Grenze_kommt_nichts_Neues_mehr_dazu()
    {
        UserPreferenceService fuerAnna = Fuer(anna);
        for (int i = 0; i < 200; i++)
        {
            Assert.True(await fuerAnna.SetAsync($"schluessel-{i}", "x"));
        }

        Assert.False(await fuerAnna.SetAsync("einer-zuviel", "x"));

        // Bestehende bleiben aenderbar -- sonst sitzt man fest.
        Assert.True(await fuerAnna.SetAsync("schluessel-0", "y"));
        Assert.Equal("y", (await fuerAnna.MineAsync())["schluessel-0"]);
    }

    private sealed class FixedUser(Guid? id) : ICurrentUser
    {
        public Guid? Id { get; } = id;
    }
}
