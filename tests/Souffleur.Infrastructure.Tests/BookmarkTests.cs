using FluentAssertions;
using Souffleur.Domain.Bookmarks;
using Souffleur.Infrastructure.Bookmarks;

namespace Souffleur.Infrastructure.Tests;

/// <summary>
/// Lesezeichen: Adressen an jedem Gegenstand, ohne dass der Dienst die
/// Gegenstaende kennt. Entscheidend ist, dass nur echte Web-Adressen
/// angenommen werden -- ein Lesezeichen soll nirgendwohin fuehren, wo es
/// Schaden anrichtet.
/// </summary>
public class BookmarkTests : AccountingTestBase
{
    private BookmarkService Service => new(Context);

    private readonly Guid aufgabe = Guid.CreateVersion7();

    [Fact]
    public async Task Ein_Lesezeichen_haengt_an_seinem_Gegenstand_und_nur_dort()
    {
        await Service.AddAsync(BookmarkTarget.Task, aufgabe, "https://example.org/recherche", "Recherche");
        await Service.AddAsync(BookmarkTarget.Task, Guid.CreateVersion7(), "https://example.org/anderes", null);

        List<Bookmark> found = await Service.ListAsync(BookmarkTarget.Task, aufgabe);

        found.Should().ContainSingle().Which.Label.Should().Be("Recherche");
    }

    [Fact]
    public async Task Ohne_Schema_wird_https_ergaenzt()
    {
        Bookmark created = await Service.AddAsync(BookmarkTarget.Contact, aufgabe, " youtube.com/watch?v=1 ", "Video");

        created.Url.Should().Be("https://youtube.com/watch?v=1");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("nur worte")]
    [InlineData("")]
    public async Task Was_keine_Web_Adresse_ist_wird_abgewiesen(string url)
    {
        Func<Task> act = () => Service.AddAsync(BookmarkTarget.Task, aufgabe, url, null);

        (await act.Should().ThrowAsync<BookmarkException>())
            .Which.Problems.Should().Contain(p => p.Contains("http"));
    }

    [Fact]
    public async Task Die_Reihenfolge_bleibt_wie_angelegt()
    {
        await Service.AddAsync(BookmarkTarget.Task, aufgabe, "https://a.example", "A");
        await Service.AddAsync(BookmarkTarget.Task, aufgabe, "https://b.example", "B");
        await Service.AddAsync(BookmarkTarget.Task, aufgabe, "https://c.example", "C");

        List<Bookmark> found = await Service.ListAsync(BookmarkTarget.Task, aufgabe);

        found.Select(b => b.Label).Should().Equal("A", "B", "C");
        found.Select(b => b.SortOrder).Should().Equal(0, 1, 2);
    }

    [Fact]
    public async Task Mit_dem_Gegenstand_verschwinden_seine_Lesezeichen()
    {
        await Service.AddAsync(BookmarkTarget.Task, aufgabe, "https://a.example", null);
        await Service.AddAsync(BookmarkTarget.Task, aufgabe, "https://b.example", null);
        Guid andere = Guid.CreateVersion7();
        await Service.AddAsync(BookmarkTarget.Task, andere, "https://c.example", null);

        (await Service.RemoveAllAsync(BookmarkTarget.Task, aufgabe)).Should().Be(2);

        (await Service.ListAsync(BookmarkTarget.Task, aufgabe)).Should().BeEmpty();
        (await Service.ListAsync(BookmarkTarget.Task, andere)).Should().ContainSingle();
        (await Service.CountAsync(BookmarkTarget.Task, [aufgabe, andere]))
            .Should().BeEquivalentTo(new Dictionary<Guid, int> { [andere] = 1 });
    }
}
