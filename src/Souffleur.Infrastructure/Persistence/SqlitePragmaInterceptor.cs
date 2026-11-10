using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Souffleur.Infrastructure.Persistence;

/// <summary>
/// Setzt die PRAGMA-Einstellungen auf jeder neuen Verbindung.
/// </summary>
/// <remarks>
/// <c>journal_mode=WAL</c> steht in der Datei und gilt dauerhaft; <c>synchronous</c>,
/// <c>foreign_keys</c> und <c>busy_timeout</c> gelten dagegen nur fuer die einzelne
/// Verbindung und muessen jedes Mal gesetzt werden.
///
/// <c>synchronous=FULL</c> kostet Schreibleistung und ist hier trotzdem richtig: Die
/// Haeuser betreiben souffleur auf einer NAS, deren Stromversorgung im Zweifel an einer
/// einfachen USV haengt. Lieber langsamer schreiben als nach einem Stromausfall eine
/// halbe Buchung in der Datei haben.
/// </remarks>
public class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    private const string Pragmas = """
        PRAGMA journal_mode = WAL;
        PRAGMA synchronous = FULL;
        PRAGMA foreign_keys = ON;
        PRAGMA busy_timeout = 10000;
        """;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        => Execute(connection);

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = Pragmas;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void Execute(DbConnection connection)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = Pragmas;
        command.ExecuteNonQuery();
    }
}
