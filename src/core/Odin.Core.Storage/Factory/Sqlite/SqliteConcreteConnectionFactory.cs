using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Nito.AsyncEx;

namespace Odin.Core.Storage.Factory.Sqlite;

internal static class SqliteConcreteConnectionFactory
{
    private static readonly AsyncLock Mutex = new ();
    private static readonly HashSet<string> PragmasExecuted = [];

    internal static async Task<DbConnection> CreateAsync(string connectionString)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        //
        // One-time per-database pragmas
        //

        if (!PragmasExecuted.Contains(connectionString))
        {
            using (await Mutex.LockAsync())
            {
                if (PragmasExecuted.Add(connectionString))
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = "PRAGMA journal_mode=WAL;";
                    await command.ExecuteNonQueryAsync();
                    command.CommandText = "PRAGMA synchronous=NORMAL;";
                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        //
        // Per-connection pragmas
        //

        await using var cmd = connection.CreateCommand();

        // Disable SQLite memory-mapped I/O which maps entire db files into process memory.
        // With many databases and pooled connections, this causes excessive native memory usage.
        cmd.CommandText = "PRAGMA mmap_size=0;";
        await cmd.ExecuteNonQueryAsync();

        // Limit page cache to ~1 MB per connection (default is ~8 MB).
        cmd.CommandText = "PRAGMA cache_size=-1000;";
        await cmd.ExecuteNonQueryAsync();

        return connection;
    }
}
