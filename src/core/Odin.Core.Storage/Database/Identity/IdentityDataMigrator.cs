using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;

namespace Odin.Core.Storage.Database.Identity;

public static class IdentityDataMigrator
{
    private const int PageSize = 100;

    public static async Task MigrateAsync(
        IdentityDatabase sourceDb,
        IdentityDatabase targetDb,
        IdentityMigrator sourceMigrator,
        IdentityMigrator targetMigrator,
        ILogger logger)
    {
        // Verify schema versions match
        var sourceVersion = await sourceMigrator.GetCurrentVersionAsync();
        var targetVersion = await targetMigrator.GetCurrentVersionAsync();

        if (sourceVersion != targetVersion)
        {
            throw new InvalidOperationException(
                $"Schema version mismatch: source is at {sourceVersion}, target is at {targetVersion}. " +
                "Both databases must be at the same schema version before migrating data.");
        }

        logger.LogInformation("Schema versions match: {version}", targetVersion);

        // Single transaction for the entire migration
        await using var cn = await targetDb.CreateScopedConnectionAsync();
        await using var tx = await targetDb.BeginStackedTransactionAsync();

        var totalRows = 0;

        totalRows += await MigrateTableAsync(sourceDb.Drives.PagingByRowIdAsync,
            r => targetDb.Drives.InsertAsync(r), logger, "Drives");

        totalRows += await MigrateTableAsync(sourceDb.DriveMainIndex.PagingByRowIdAsync,
            r => targetDb.DriveMainIndex.InsertAsync(r), logger, "DriveMainIndex");

        totalRows += await MigrateTableAsync(sourceDb.DriveTransferHistory.PagingByRowIdAsync,
            r => targetDb.DriveTransferHistory.InsertAsync(r), logger, "DriveTransferHistory");

        totalRows += await MigrateTableAsync(sourceDb.DriveAclIndex.PagingByRowIdAsync,
            r => targetDb.DriveAclIndex.InsertAsync(r), logger, "DriveAclIndex");

        totalRows += await MigrateTableAsync(sourceDb.DriveTagIndex.PagingByRowIdAsync,
            r => targetDb.DriveTagIndex.InsertAsync(r), logger, "DriveTagIndex");

        totalRows += await MigrateTableAsync(sourceDb.DriveLocalTagIndex.PagingByRowIdAsync,
            r => targetDb.DriveLocalTagIndex.InsertAsync(r), logger, "DriveLocalTagIndex");

        totalRows += await MigrateTableAsync(sourceDb.DriveReactions.PagingByRowIdAsync,
            r => targetDb.DriveReactions.InsertAsync(r), logger, "DriveReactions");

        totalRows += await MigrateTableAsync(sourceDb.AppNotifications.PagingByRowIdAsync,
            r => targetDb.AppNotifications.InsertAsync(r), logger, "AppNotifications");

        totalRows += await MigrateTableAsync(sourceDb.ClientRegistrations.PagingByRowIdAsync,
            r => targetDb.ClientRegistrations.InsertAsync(r), logger, "ClientRegistrations");

        totalRows += await MigrateTableAsync(sourceDb.Circle.PagingByRowIdAsync,
            r => targetDb.Circle.InsertAsync(r), logger, "Circle");

        totalRows += await MigrateTableAsync(sourceDb.CircleMember.PagingByRowIdAsync,
            r => targetDb.CircleMember.InsertAsync(r), logger, "CircleMember");

        totalRows += await MigrateTableAsync(sourceDb.Connections.PagingByRowIdAsync,
            r => targetDb.Connections.InsertAsync(r), logger, "Connections");

        totalRows += await MigrateTableAsync(sourceDb.AppGrants.PagingByRowIdAsync,
            r => targetDb.AppGrants.InsertAsync(r), logger, "AppGrants");

        totalRows += await MigrateTableAsync(sourceDb.ImFollowing.PagingByRowIdAsync,
            r => targetDb.ImFollowing.InsertAsync(r), logger, "ImFollowing");

        totalRows += await MigrateTableAsync(sourceDb.FollowsMe.PagingByRowIdAsync,
            r => targetDb.FollowsMe.InsertAsync(r), logger, "FollowsMe");

        totalRows += await MigrateTableAsync(sourceDb.Inbox.PagingByRowIdAsync,
            r => targetDb.Inbox.InsertAsync(r), logger, "Inbox");

        totalRows += await MigrateTableAsync(sourceDb.Outbox.PagingByRowIdAsync,
            r => targetDb.Outbox.InsertAsync(r), logger, "Outbox");

        totalRows += await MigrateTableAsync(sourceDb.KeyValue.PagingByRowIdAsync,
            r => targetDb.KeyValue.InsertAsync(r), logger, "KeyValue");

        totalRows += await MigrateTableAsync(sourceDb.KeyTwoValue.PagingByRowIdAsync,
            r => targetDb.KeyTwoValue.InsertAsync(r), logger, "KeyTwoValue");

        totalRows += await MigrateTableAsync(sourceDb.KeyThreeValue.PagingByRowIdAsync,
            r => targetDb.KeyThreeValue.InsertAsync(r), logger, "KeyThreeValue");

        totalRows += await MigrateTableAsync(sourceDb.KeyUniqueThreeValue.PagingByRowIdAsync,
            r => targetDb.KeyUniqueThreeValue.InsertAsync(r), logger, "KeyUniqueThreeValue");

        // Nonce: skip expired records since InsertAsync rejects them
        totalRows += await MigrateTableAsync(sourceDb.Nonce.PagingByRowIdAsync,
            async r =>
            {
                if (r.expiration > UnixTimeUtc.Now())
                    await targetDb.Nonce.InsertAsync(r);
            }, logger, "Nonce");

        tx.Commit();

        logger.LogInformation("Migrated {count} total rows", totalRows);
    }

    //

    private static async Task<int> MigrateTableAsync<TRecord>(
        Func<int, long?, Task<(List<TRecord>, long?)>> readPage,
        Func<TRecord, Task<int>> writeRecord,
        ILogger logger,
        string tableName)
    {
        var totalRows = 0;
        long? cursor = null;

        do
        {
            var (records, nextCursor) = await readPage(PageSize, cursor);

            foreach (var record in records)
            {
                await writeRecord(record);
                totalRows++;
            }

            cursor = nextCursor;
        } while (cursor != null);

        if (totalRows > 0)
            logger.LogInformation("  {table}: {count} rows", tableName, totalRows);

        return totalRows;
    }

    // Overload for write actions that don't return a count (e.g. Nonce with filtering)
    private static async Task<int> MigrateTableAsync<TRecord>(
        Func<int, long?, Task<(List<TRecord>, long?)>> readPage,
        Func<TRecord, Task> writeRecord,
        ILogger logger,
        string tableName)
    {
        var totalRows = 0;
        long? cursor = null;

        do
        {
            var (records, nextCursor) = await readPage(PageSize, cursor);

            foreach (var record in records)
            {
                await writeRecord(record);
                totalRows++;
            }

            cursor = nextCursor;
        } while (cursor != null);

        if (totalRows > 0)
            logger.LogInformation("  {table}: {count} rows", tableName, totalRows);

        return totalRows;
    }
}
