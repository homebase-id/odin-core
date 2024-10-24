using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Nito.AsyncEx;

namespace Odin.Core.Storage.Database.Connection;

internal static class SqliteConcreteConnectionFactory
{
    private static readonly AsyncLock Mutex = new ();
    private static bool _initialized;

    internal static async Task<DbConnection> Create(string connectionString)
    {
        // SEB:TODO do we need explicit retry logic here?
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        if (!_initialized)
        {
            using (await Mutex.LockAsync())
            {
                if (!_initialized)
                {
                    _initialized = true;
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
