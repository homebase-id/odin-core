using System.Data;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Storage.Exceptions;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.System.Table;

#nullable enable

public class TableSettings(ScopedSystemConnectionFactory scopedConnectionFactory)
    : TableSettingsCRUD(scopedConnectionFactory)
{
    private readonly ScopedSystemConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    /// <summary>
    /// Atomically advances a monotonic counter kept in the row's <c>modified</c> stamp and returns
    /// the value it advanced from and the value it has now. Must run inside the caller's
    /// transaction: the row is locked for update before it is read, so a concurrent bump cannot
    /// slip between the read and the write, which a plain read-then-upsert allows under READ
    /// COMMITTED. On SQLite the write lock already serialises writers, so FOR UPDATE is omitted.
    /// </summary>
    public async Task<(long previous, long current)> BumpMonotonicAsync(string key)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();

        await using (var insert = cn.CreateCommand())
        {
            var now = insert.SqlNow();
            insert.CommandText =
                $"INSERT INTO Settings (key,value,created,modified) VALUES (@key,'',{now},{now}) ON CONFLICT (key) DO NOTHING;";
            insert.AddParameter("@key", DbType.String, key);
            await insert.ExecuteNonQueryAsync();
        }

        long previous;
        await using (var read = cn.CreateCommand())
        {
            read.CommandText = read.DatabaseType == DatabaseType.Postgres
                ? "SELECT modified FROM Settings WHERE key = @key FOR UPDATE;"
                : "SELECT modified FROM Settings WHERE key = @key;";
            read.AddParameter("@key", DbType.String, key);
            var value = await read.ExecuteScalarAsync();
            previous = value is long l ? l : throw new OdinDatabaseException(read.DatabaseType, $"Settings row {key} vanished during bump");
        }

        await using (var bump = cn.CreateCommand())
        {
            var now = bump.SqlNow();
            bump.CommandText =
                $"UPDATE Settings SET modified = {bump.SqlMax()}(modified + 1, {now}) WHERE key = @key RETURNING modified;";
            bump.AddParameter("@key", DbType.String, key);
            var value = await bump.ExecuteScalarAsync();
            var current = value is long c ? c : throw new OdinDatabaseException(bump.DatabaseType, $"Settings row {key} vanished during bump");
            return (previous, current);
        }
    }
}
