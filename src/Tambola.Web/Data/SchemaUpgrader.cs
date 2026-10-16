using Microsoft.EntityFrameworkCore;

namespace Tambola.Web.Data;

/// <summary>
/// The database is created with EnsureCreated (no EF migrations), so columns added in later versions are
/// patched in here. Safe to run on every start: each step is skipped when it is already applied.
/// </summary>
public static class SchemaUpgrader
{
    public static async Task UpgradeAsync(TambolaDb db)
    {
        // New databases already have everything (EnsureCreated builds the current model).
        await AddColumnAsync(db, "Rooms", "MaxWrongClaims", "INTEGER NOT NULL DEFAULT 1");   // existing rooms keep the old "one strike" rule
        await AddColumnAsync(db, "Rooms", "IsDeleted", "INTEGER NOT NULL DEFAULT 0");
        await AddColumnAsync(db, "Rooms", "DeletedAt", "TEXT NULL");
        await AddColumnAsync(db, "Rooms", "DeletedBy", "TEXT NULL");
        await AddColumnAsync(db, "Employees", "PasswordHash", "TEXT NULL");
        await AddColumnAsync(db, "Employees", "MustChangePassword", "INTEGER NOT NULL DEFAULT 0");
        // Unique ticket grid per room (also makes ticket purchase O(1) instead of loading every grid).
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_Tickets_RoomId_Grid ON Tickets (RoomId, Grid);");
        // v1.2: a player can win the same prize once per ticket, so uniqueness moves from (prize, player) to (prize, ticket).
        if (await TableExistsAsync(db, "PrizeWinners"))
        {
            await db.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS IX_PrizeWinners_PrizeId_EmployeeId;");
            await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_PrizeWinners_PrizeId_TicketId ON PrizeWinners (PrizeId, TicketId);");
        }
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS AppSettings (\"Key\" TEXT NOT NULL PRIMARY KEY, \"Value\" TEXT NOT NULL);");
    }

    private static async Task<bool> TableExistsAsync(TambolaDb db, string table)
    {
        var conn = db.Database.GetDbConnection();
        var wasClosed = conn.State != System.Data.ConnectionState.Open;
        if (wasClosed) await conn.OpenAsync();
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{table}'";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        finally { if (wasClosed) await conn.CloseAsync(); }
    }

    private static async Task AddColumnAsync(TambolaDb db, string table, string column, string definition)
    {
        var conn = db.Database.GetDbConnection();
        var wasClosed = conn.State != System.Data.ConnectionState.Open;
        if (wasClosed) await conn.OpenAsync();
        try
        {
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}'";
                if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0) return;
            }
            await using var alter = conn.CreateCommand();
            alter.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition}";
            await alter.ExecuteNonQueryAsync();
        }
        finally { if (wasClosed) await conn.CloseAsync(); }
    }
}
