using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Common;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Tests;

/// <summary>
/// Die GoBD-Unveraenderbarkeit haengt an der Domaenenschicht, nicht an SQLite.
/// Diese Tests halten fest, dass der DbContext sie durchsetzt.
/// </summary>
public class ImmutabilityTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private TestDbContext context = null!;

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        context = new TestDbContext(
            new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await context.DisposeAsync();
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task Entwurf_laesst_sich_aendern()
    {
        var booking = new TestBooking { Amount = 100m };
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();

        booking.Amount = 120m;

        Func<Task> act = () => context.SaveChangesAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Festgeschriebener_Datensatz_laesst_sich_nicht_aendern()
    {
        var booking = new TestBooking { Amount = 100m, CommittedAt = DateTimeOffset.UtcNow };
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();

        booking.Amount = 120m;

        Func<Task> act = () => context.SaveChangesAsync();
        ImmutableRecordException exception = (await act.Should().ThrowAsync<ImmutableRecordException>()).Which;
        exception.AttemptedState.Should().Be(EntityState.Modified);
        exception.Message.Should().Contain("stornieren");
    }

    [Fact]
    public async Task Festgeschriebener_Datensatz_laesst_sich_nicht_loeschen()
    {
        var booking = new TestBooking { Amount = 100m, CommittedAt = DateTimeOffset.UtcNow };
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();

        context.Bookings.Remove(booking);

        Func<Task> act = () => context.SaveChangesAsync();
        ImmutableRecordException exception = (await act.Should().ThrowAsync<ImmutableRecordException>()).Which;
        exception.AttemptedState.Should().Be(EntityState.Deleted);
    }

    [Fact]
    public async Task Audit_Felder_werden_gesetzt_und_CreatedAt_bleibt_stehen()
    {
        var booking = new TestBooking { Amount = 100m };
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();

        DateTimeOffset created = booking.CreatedAt;
        created.Should().NotBe(default);
        booking.ModifiedAt.Should().BeNull();

        booking.Amount = 120m;
        await context.SaveChangesAsync();

        booking.CreatedAt.Should().Be(created);
        booking.ModifiedAt.Should().NotBeNull();
    }
}

public class TestBooking : Entity, IImmutable
{
    public decimal Amount { get; set; }

    public DateTimeOffset? CommittedAt { get; set; }
}

/// <summary>
/// Minimaler Kontext mit derselben SaveChanges-Logik wie <see cref="SouffleurDbContext"/>,
/// damit der Test ohne Identity-Tabellen auskommt.
/// </summary>
public class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<TestBooking> Bookings => Set<TestBooking>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        AuditBehaviour.Apply(ChangeTracker, TimeProvider.System.GetUtcNow(), userId: null);
        return base.SaveChangesAsync(cancellationToken);
    }
}
