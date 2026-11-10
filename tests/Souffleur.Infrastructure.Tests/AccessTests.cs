using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Souffleur.Domain.Contacts;
using Souffleur.Domain.Identity;
using Souffleur.Infrastructure.Identity;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Tests;

/// <summary>
/// Rechte kommen aus eigenen Rollen und aus den Rollen der Gruppen. Beides
/// zaehlt, und beides muss sich wieder entziehen lassen.
/// </summary>
public class AccessTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly Guid anna = Guid.CreateVersion7();
    private readonly Guid ben = Guid.CreateVersion7();

    private SouffleurDbContext context = null!;
    private AccessService access = null!;
    private Role buchhaltung = null!;
    private Role technik = null!;

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        context = new SouffleurDbContext(
            new DbContextOptionsBuilder<SouffleurDbContext>().UseSqlite(connection).Options,
            TimeProvider.System,
            new NobodyInParticular());
        await context.Database.EnsureCreatedAsync();

        context.Users.AddRange(
            new User { Id = anna, GivenName = "Anna", Surname = "Krause", UserName = "anna" },
            new User { Id = ben, GivenName = "Ben", Surname = "Schröder", UserName = "ben" });

        buchhaltung = new Role { Id = Guid.CreateVersion7(), Name = "Buchhaltung" };
        technik = new Role { Id = Guid.CreateVersion7(), Name = "Technik" };
        context.Roles.AddRange(buchhaltung, technik);

        context.RolePermissions.AddRange(
            new RolePermission { RoleId = buchhaltung.Id, Permission = Permission.FinanceBook },
            new RolePermission { RoleId = buchhaltung.Id, Permission = Permission.FinanceRead },
            new RolePermission { RoleId = technik.Id, Permission = Permission.ResourcesManage });

        await context.SaveChangesAsync();
        access = new AccessService(context);
    }

    public async Task DisposeAsync()
    {
        await context.DisposeAsync();
        await connection.DisposeAsync();
    }

    private async Task GibRolleAsync(Guid userId, Role role)
    {
        context.UserRoles.Add(new IdentityUserRole<Guid> { UserId = userId, RoleId = role.Id });
        await context.SaveChangesAsync();
    }

    private async Task<Group> GruppeAsync(string name, Role role, params Guid[] mitglieder)
    {
        var gruppe = new Group { Name = name };
        context.Groups.Add(gruppe);
        context.GroupRoles.Add(new GroupRole { GroupId = gruppe.Id, RoleId = role.Id });
        foreach (Guid userId in mitglieder)
        {
            context.GroupMembers.Add(new GroupMember { GroupId = gruppe.Id, UserId = userId });
        }

        await context.SaveChangesAsync();
        return gruppe;
    }

    [Fact]
    public async Task Ohne_Rolle_und_Gruppe_darf_man_nichts()
    {
        Assert.Empty(await access.PermissionsOfAsync(anna));
    }

    [Fact]
    public async Task Eine_eigene_Rolle_bringt_ihre_Rechte()
    {
        await GibRolleAsync(anna, buchhaltung);

        IReadOnlySet<Permission> rechte = await access.PermissionsOfAsync(anna);

        Assert.Contains(Permission.FinanceBook, rechte);
        Assert.Contains(Permission.FinanceRead, rechte);
        Assert.DoesNotContain(Permission.ResourcesManage, rechte);
    }

    [Fact]
    public async Task Eine_Gruppe_bringt_ihre_Rechte_mit()
    {
        await GruppeAsync("Werkstatt", technik, anna);

        Assert.Contains(Permission.ResourcesManage, await access.PermissionsOfAsync(anna));
        Assert.Empty(await access.PermissionsOfAsync(ben));
    }

    [Fact]
    public async Task Eigene_Rollen_und_Gruppen_addieren_sich()
    {
        await GibRolleAsync(anna, buchhaltung);
        await GruppeAsync("Werkstatt", technik, anna);

        IReadOnlySet<Permission> rechte = await access.PermissionsOfAsync(anna);

        Assert.Equal(3, rechte.Count);
        Assert.Contains(Permission.FinanceBook, rechte);
        Assert.Contains(Permission.ResourcesManage, rechte);
    }

    [Fact]
    public async Task Dasselbe_Recht_aus_zwei_Richtungen_zaehlt_einmal()
    {
        await GibRolleAsync(anna, technik);
        await GruppeAsync("Werkstatt", technik, anna);

        Assert.Single(await access.PermissionsOfAsync(anna));
    }

    [Fact]
    public async Task Eine_stillgelegte_Gruppe_vergibt_keine_Rechte_mehr()
    {
        Group gruppe = await GruppeAsync("Werkstatt", technik, anna);
        Assert.NotEmpty(await access.PermissionsOfAsync(anna));

        gruppe.IsActive = false;
        await context.SaveChangesAsync();

        Assert.Empty(await access.PermissionsOfAsync(anna));
    }

    [Fact]
    public async Task Wer_von_einer_Rollenaenderung_betroffen_ist_wird_gefunden()
    {
        await GibRolleAsync(anna, buchhaltung);
        await GruppeAsync("Werkstatt", buchhaltung, ben);

        IReadOnlyList<Guid> betroffen = await access.AffectedByRoleAsync(buchhaltung.Id);

        Assert.Equal(2, betroffen.Count);
        Assert.Contains(anna, betroffen);
        Assert.Contains(ben, betroffen);
    }

    [Fact]
    public async Task Hinter_einer_Gruppe_stehen_nur_verbundene_aktive_Kontakte()
    {
        Contact annaKontakt = Kontakt("Krause, Anna", anna);
        Contact benKontakt = Kontakt("Schröder, Ben", ben, aktiv: false);
        Contact ohneKonto = Kontakt("Weber, Clara", userId: null);
        context.Contacts.AddRange(annaKontakt, benKontakt, ohneKonto);
        await context.SaveChangesAsync();

        Group gruppe = await GruppeAsync("Technik", technik, anna, ben);

        IReadOnlyList<Guid> kontakte = await access.ContactsOfGroupAsync(gruppe.Id);

        // Ben ist in der Gruppe, sein Kontakt aber abgeschaltet; Clara hat
        // gar kein Konto. Uebrig bleibt Anna.
        Assert.Equal([annaKontakt.Id], kontakte);
    }

    private static Contact Kontakt(string name, Guid? userId, bool aktiv = true) => new()
    {
        Kind = ContactKind.Person,
        Name = name,
        UserId = userId,
        IsActive = aktiv,
        Roles = [new ContactRoleAssignment { Role = ContactRole.Staff }],
    };

    private sealed class NobodyInParticular : ICurrentUser
    {
        public Guid? Id => null;
    }
}
