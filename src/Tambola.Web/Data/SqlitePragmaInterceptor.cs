using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tambola.Web.Data;

/// <summary>
/// Applies per-connection SQLite settings on every opened connection (PRAGMAs are not persistent).
/// synchronous=NORMAL is the recommended, crash-safe setting for WAL and removes an fsync from every commit.
/// </summary>
public class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    private const string Sql = "PRAGMA synchronous=NORMAL;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = Sql;
        cmd.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = Sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
