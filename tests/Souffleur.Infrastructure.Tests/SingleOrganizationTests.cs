using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace Souffleur.Infrastructure.Tests;

/// <summary>
/// souffleur ist nicht mandantenfaehig (ADR-0003). Diese Tests halten fest, dass
/// ein zweites Haus nicht in dieselbe Datenbank passt -- durchgesetzt von SQLite,
/// nicht nur von einer Zusicherung im Code.
/// </summary>
public class SingleOrganizationTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        await ExecuteAsync("""
            CREATE TABLE Organizations (
                Id TEXT NOT NULL PRIMARY KEY,
                Name TEXT NOT NULL,
                CONSTRAINT CK_Organization_Singleton
                    CHECK (Id = '00000000-0000-0000-0000-000000000001')
            );
            """);
    }

    public async Task DisposeAsync() => await connection.DisposeAsync();

    [Fact]
    public async Task Das_eine_Haus_laesst_sich_anlegen()
    {
        Func<Task> act = () => InsertAsync("00000000-0000-0000-0000-000000000001", "Theater am Kanal");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Ein_zweites_Haus_weist_die_Datenbank_ab()
    {
        await InsertAsync("00000000-0000-0000-0000-000000000001", "Theater am Kanal");

        Func<Task> act = () => InsertAsync("00000000-0000-0000-0000-000000000002", "Kollektiv Nord");

        (await act.Should().ThrowAsync<SqliteException>())
            .Which.Message.Should().Contain("CK_Organization_Singleton");
    }

    private Task InsertAsync(string id, string name) =>
        ExecuteAsync($"INSERT INTO Organizations (Id, Name) VALUES ('{id}', '{name}');");

    private async Task ExecuteAsync(string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
