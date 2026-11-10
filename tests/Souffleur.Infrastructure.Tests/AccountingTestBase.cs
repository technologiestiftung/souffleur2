using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Accounting;
using Souffleur.Infrastructure.Accounting;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Tests;

/// <summary>
/// Eine echte SQLite-Datenbank im Arbeitsspeicher, mit echtem Schema. Keine
/// Attrappe -- die Check-Constraints sollen mitgeprüft werden.
/// </summary>
public abstract class AccountingTestBase : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");

    protected SouffleurDbContext Context { get; private set; } = null!;

    protected JournalService Journal { get; private set; } = null!;

    protected Account Bank { get; private set; } = null!;

    protected Account Rent { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();

        Context = new SouffleurDbContext(
            new DbContextOptionsBuilder<SouffleurDbContext>().UseSqlite(connection).Options,
            TimeProvider.System,
            new NoCurrentUser());
        await Context.Database.EnsureCreatedAsync();

        // Zwei Konten, mit denen sich arbeiten lässt. Die Nummern sind SKR03-üblich,
        // der echte Kontenrahmen kommt vom Steuerbüro.
        Bank = new Account { Number = "1200", Name = "Bank", Type = AccountType.Asset };
        Rent = new Account { Number = "4210", Name = "Miete", Type = AccountType.Expense };
        Context.Accounts.AddRange(Bank, Rent);
        await Context.SaveChangesAsync();

        Journal = new JournalService(Context, TimeProvider.System, new NoCurrentUser());
    }

    public async Task DisposeAsync()
    {
        await Context.DisposeAsync();
        await connection.DisposeAsync();
    }

    /// <summary>Eine gültige Buchung: Miete vom Bankkonto bezahlt.</summary>
    protected JournalEntry RentPayment(decimal amount = 1500m, int year = 2026) => new()
    {
        FiscalYear = year,
        Date = new DateOnly(year, 3, 1),
        Description = "Miete März",
        Lines =
        [
            new JournalLine { AccountId = Rent.Id, Debit = amount },
            new JournalLine { AccountId = Bank.Id, Credit = amount },
        ],
    };

    private sealed class NoCurrentUser : ICurrentUser
    {
        public Guid? Id => null;
    }
}
