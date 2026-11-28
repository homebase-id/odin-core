using System.Collections.Concurrent;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Odin.Core.Storage.Factory.Sqlite;

internal static class SqliteConcreteConnectionFactory
{
    private static readonly ConcurrentDictionary<string, byte> PragmasExecuted = new();

    internal const long JournalSizeLimitBytes = 16 * 1024 * 1024;

    internal static async Task<DbConnection> CreateAsync(string connectionString)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        //
        // One-time per-database pragmas (persistent in the database file header)
        //

        if (!PragmasExecuted.ContainsKey(connectionString))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL;";
            await command.ExecuteNonQueryAsync();
            PragmasExecuted.TryAdd(connectionString, 0);
        }

        //
        // Per-connection pragmas (not persistent, must be set on every connection)
        //

        await using var cmd = connection.CreateCommand();

        // NORMAL is the recommended durability level in WAL mode; default is FULL.
        cmd.CommandText = "PRAGMA synchronous=NORMAL;";
        await cmd.ExecuteNonQueryAsync();

        // Disable SQLite memory-mapped I/O which maps entire db files into process memory.
        // With many databases and pooled connections, this causes excessive native memory usage.
        cmd.CommandText = "PRAGMA mmap_size=0;";
        await cmd.ExecuteNonQueryAsync();

        // Limit page cache to ~1000 KiB per connection (default is ~8 MB).
        cmd.CommandText = "PRAGMA cache_size=-1000;";
        await cmd.ExecuteNonQueryAsync();

        // Truncate the WAL file to this limit when it is reset. Without one it stays at the largest size it ever reached
        // (it grows while readers hold the WAL) until the last connection to the database closes, which with our pool is
        // usually when the host stops. The limit is above the normal cycle (the autocheckpoint at 1000 pages, ~4 MB), so a
        // normal reset doesn't truncate.
        cmd.CommandText = $"PRAGMA journal_size_limit={JournalSizeLimitBytes};";
        await cmd.ExecuteNonQueryAsync();

        // Let SQLite wait for a busy lock itself, retrying within milliseconds of its release. Without this,
        // Microsoft.Data.Sqlite retries a busy statement only every 150 ms, so a waiter keeps losing the lock to writers
        // that arrive in between. The command timeout's budget (30 s by default), so nothing waits longer or fails sooner.
        cmd.CommandText = $"PRAGMA busy_timeout={connection.DefaultTimeout * 1000};";
        await cmd.ExecuteNonQueryAsync();

        return connection;
    }
}
