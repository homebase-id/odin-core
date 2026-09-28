using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.Identity;

#nullable enable

public partial class IdentityDatabase
{
    /// <summary>
    /// Deletes every row <paramref name="identityId"/> owns in the identity database, in one transaction, and fails
    /// if any survive (#1792). On PostgreSQL every identity shares one database, so deleting the tenant folder leaves
    /// these rows behind; on SQLite the database is a file in that folder.
    /// </summary>
    public async Task PurgeIdentityAsync(Guid identityId)
    {
        if (identityId == Guid.Empty)
        {
            throw new OdinSystemException("I just stopped you in wiping every identity's rows (missing id)");
        }

        await using var tx = await BeginStackedTransactionAsync();
        await using var cn = await CreateScopedConnectionAsync();

        var tables = await GetPurgeTablesAsync();
        foreach (var table in tables)
        {
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = $"DELETE FROM {table} WHERE identityId = @identityId;";
            cmd.AddParameter("@identityId", DbType.Binary, identityId);
            await cmd.ExecuteNonQueryAsync();
        }

        var remaining = await CountIdentityRowsAsync(identityId, tables);
        if (remaining > 0)
        {
            throw new OdinSystemException($"Purging identity {identityId} left {remaining} rows in the identity database");
        }

        tx.Commit();
    }

    /// <summary>
    /// How many rows <paramref name="identityId"/> owns across <see cref="GetPurgeTablesAsync"/>.
    /// </summary>
    public async Task<long> CountIdentityRowsAsync(Guid identityId)
    {
        return await CountIdentityRowsAsync(identityId, await GetPurgeTablesAsync());
    }

    /// <summary>
    /// The tables an identity's rows live in: the generated <see cref="ExportableTables"/>, so one the generator adds
    /// is covered, and the copies migrations keep of them. A migration renames the table it replaces to
    /// <c>{Table}MigrationsV{version}</c> for its down step, so those copies hold rows as of that migration.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetPurgeTablesAsync()
    {
        var backupPrefixes = ExportableTables.Select(t => t.ToLowerInvariant() + "migrationsv").ToList();

        await using var cn = await CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = cmd.DatabaseType == DatabaseType.Sqlite
            ? "SELECT m.name FROM sqlite_master m JOIN pragma_table_info(m.name) p " +
              "WHERE m.type = 'table' AND lower(p.name) = 'identityid';"
            : "SELECT table_name FROM information_schema.columns " +
              "WHERE table_schema = current_schema() AND lower(column_name) = 'identityid';";

        var backups = new List<string>();
        await using (var rdr = await cmd.ExecuteReaderAsync())
        {
            while (await rdr.ReadAsync())
            {
                var name = rdr.GetString(0);
                if (backupPrefixes.Any(prefix => name.ToLowerInvariant().StartsWith(prefix)))
                {
                    backups.Add(name);
                }
            }
        }

        return [..ExportableTables, ..backups];
    }

    private async Task<long> CountIdentityRowsAsync(Guid identityId, IEnumerable<string> tables)
    {
        await using var cn = await CreateScopedConnectionAsync();

        long total = 0;
        foreach (var table in tables)
        {
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {table} WHERE identityId = @identityId;";
            cmd.AddParameter("@identityId", DbType.Binary, identityId);
            total += Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }

        return total;
    }
}
