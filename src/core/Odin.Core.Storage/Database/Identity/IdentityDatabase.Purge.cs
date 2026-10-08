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
        long remaining = 0;
        foreach (var table in tables)
        {
            await using var delete = cn.CreateCommand();
            delete.CommandText = $"DELETE FROM {table} WHERE identityId = @identityId;";
            delete.AddParameter("@identityId", DbType.Binary, identityId);
            await delete.ExecuteNonQueryAsync();

            await using var count = cn.CreateCommand();
            count.CommandText = $"SELECT COUNT(*) FROM {table} WHERE identityId = @identityId;";
            count.AddParameter("@identityId", DbType.Binary, identityId);
            remaining += Convert.ToInt64(await count.ExecuteScalarAsync());
        }

        if (remaining > 0)
        {
            throw new OdinSystemException($"Purging identity {identityId} left {remaining} rows in the identity database");
        }

        tx.Commit();
    }

    /// <summary>
    /// The tables an identity's rows live in: the generated <see cref="ExportableTables"/>, so one the generator adds
    /// is covered, and the copies migrations keep of them (<see cref="MigrationBase.BackupTableInfix"/>), which hold
    /// rows as of that migration.
    /// </summary>
    internal async Task<IReadOnlyList<string>> GetPurgeTablesAsync()
    {
        var backups = (await TablesWithIdentityIdAsync()).Where(name => ExportableTables.Any(table =>
            name.StartsWith(table + MigrationBase.BackupTableInfix, StringComparison.OrdinalIgnoreCase)));

        return [..ExportableTables, ..backups];
    }

    /// <summary>
    /// Every table in this database with an identityId column.
    /// </summary>
    internal async Task<List<string>> TablesWithIdentityIdAsync()
    {
        await using var cn = await CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = cmd.DatabaseType == DatabaseType.Sqlite
            ? "SELECT m.name FROM sqlite_master m JOIN pragma_table_info(m.name) p " +
              "WHERE m.type = 'table' AND lower(p.name) = 'identityid';"
            : "SELECT table_name FROM information_schema.columns " +
              "WHERE table_schema = current_schema() AND lower(column_name) = 'identityid';";

        var tables = new List<string>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            tables.Add(rdr.GetString(0));
        }

        return tables;
    }
}
