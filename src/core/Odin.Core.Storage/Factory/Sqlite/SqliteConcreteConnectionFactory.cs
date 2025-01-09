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
        // SEB:TODO do we need explicit retry logic here?
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

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
        return connection;
    }
}
