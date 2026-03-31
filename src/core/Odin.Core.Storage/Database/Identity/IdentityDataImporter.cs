using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Time;

namespace Odin.Core.Storage.Database.Identity;

public static class IdentityDataImporter
{
    private const int PageSize = 100;

    public static async Task ImportAsync(
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
                "Both databases must be at the same schema version before importing data.");
        }

        logger.LogInformation("Schema versions match: {version}", targetVersion);

        // Single transaction for the entire import
        await using var cn = await targetDb.CreateScopedConnectionAsync();
        await using var tx = await targetDb.BeginStackedTransactionAsync();

        var totalRows = 0;

        // Drives
        totalRows += await ImportTableAsync(sourceDb.Drives.PagingByRowIdAsync,
            r => targetDb.Drives.InsertAsync(r), logger, sourceDb.Drives.TableName);

        // DriveMainIndex
        totalRows += await ImportTableAsync(sourceDb.DriveMainIndex.PagingByRowIdAsync,
            r => targetDb.DriveMainIndex.InsertAsync(r), logger, sourceDb.DriveMainIndex.TableName);

        // DriveTransferHistory
        totalRows += await ImportTableAsync(sourceDb.DriveTransferHistory.PagingByRowIdAsync,
            r => targetDb.DriveTransferHistory.InsertAsync(r), logger, sourceDb.DriveTransferHistory.TableName);

        // DriveAclIndex
        totalRows += await ImportTableAsync(sourceDb.DriveAclIndex.PagingByRowIdAsync,
            r => targetDb.DriveAclIndex.InsertAsync(r), logger, sourceDb.DriveAclIndex.TableName);

        // DriveTagIndex
        totalRows += await ImportTableAsync(sourceDb.DriveTagIndex.PagingByRowIdAsync,
            r => targetDb.DriveTagIndex.InsertAsync(r), logger, sourceDb.DriveTagIndex.TableName);

        // DriveLocalTagIndex
        totalRows += await ImportTableAsync(sourceDb.DriveLocalTagIndex.PagingByRowIdAsync,
            r => targetDb.DriveLocalTagIndex.InsertAsync(r), logger, sourceDb.DriveLocalTagIndex.TableName);

        // DriveReactions
        totalRows += await ImportTableAsync(sourceDb.DriveReactions.PagingByRowIdAsync,
            r => targetDb.DriveReactions.InsertAsync(r), logger, sourceDb.DriveReactions.TableName);

        // AppNotifications
        totalRows += await ImportTableAsync(sourceDb.AppNotifications.PagingByRowIdAsync,
            r => targetDb.AppNotifications.InsertAsync(r), logger, sourceDb.AppNotifications.TableName);

        // ClientRegistrations
        totalRows += await ImportTableAsync(sourceDb.ClientRegistrations.PagingByRowIdAsync,
            r => targetDb.ClientRegistrations.InsertAsync(r), logger, sourceDb.ClientRegistrations.TableName);

        // Circle
        totalRows += await ImportTableAsync(sourceDb.Circle.PagingByRowIdAsync,
            r => targetDb.Circle.InsertAsync(r), logger, sourceDb.Circle.TableName);

        // CircleMember
        totalRows += await ImportTableAsync(sourceDb.CircleMember.PagingByRowIdAsync,
            r => targetDb.CircleMember.InsertAsync(r), logger, sourceDb.CircleMember.TableName);

        // Connections
        totalRows += await ImportTableAsync(sourceDb.Connections.PagingByRowIdAsync,
            r => targetDb.Connections.InsertAsync(r), logger, sourceDb.Connections.TableName);

        // AppGrants
        totalRows += await ImportTableAsync(sourceDb.AppGrants.PagingByRowIdAsync,
            r => targetDb.AppGrants.InsertAsync(r), logger, sourceDb.AppGrants.TableName);

        // ImFollowing
        totalRows += await ImportTableAsync(sourceDb.ImFollowing.PagingByRowIdAsync,
            r => targetDb.ImFollowing.InsertAsync(r), logger, sourceDb.ImFollowing.TableName);

        // FollowsMe
        totalRows += await ImportTableAsync(sourceDb.FollowsMe.PagingByRowIdAsync,
            r => targetDb.FollowsMe.InsertAsync(r), logger, sourceDb.FollowsMe.TableName);

        // Inbox
        totalRows += await ImportTableAsync(sourceDb.Inbox.PagingByRowIdAsync,
            r => targetDb.Inbox.InsertAsync(r), logger, sourceDb.Inbox.TableName);

        // Outbox
        totalRows += await ImportTableAsync(sourceDb.Outbox.PagingByRowIdAsync,
            r => targetDb.Outbox.InsertAsync(r), logger, sourceDb.Outbox.TableName);

        // KeyValue
        totalRows += await ImportTableAsync(sourceDb.KeyValue.PagingByRowIdAsync,
            r => targetDb.KeyValue.InsertAsync(r), logger, sourceDb.KeyValue.TableName);

        // KeyTwoValue
        totalRows += await ImportTableAsync(sourceDb.KeyTwoValue.PagingByRowIdAsync,
            r => targetDb.KeyTwoValue.InsertAsync(r), logger, sourceDb.KeyTwoValue.TableName);

        // KeyThreeValue
        totalRows += await ImportTableAsync(sourceDb.KeyThreeValue.PagingByRowIdAsync,
            r => targetDb.KeyThreeValue.InsertAsync(r), logger, sourceDb.KeyThreeValue.TableName);

        // KeyUniqueThreeValue
        totalRows += await ImportTableAsync(sourceDb.KeyUniqueThreeValue.PagingByRowIdAsync,
            r => targetDb.KeyUniqueThreeValue.InsertAsync(r), logger, sourceDb.KeyUniqueThreeValue.TableName);

        // Nonce (skip expired records since InsertAsync rejects them)
        totalRows += await ImportTableAsync(sourceDb.Nonce.PagingByRowIdAsync,
            async r =>
            {
                if (r.expiration > UnixTimeUtc.Now())
                    return await targetDb.Nonce.InsertAsync(r);
                return 0;
            }, logger, sourceDb.Nonce.TableName);

        tx.Commit();

        logger.LogInformation("Imported {count} total rows", totalRows);
    }

    //

    private static async Task<int> ImportTableAsync<TRecord>(
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
                totalRows += await writeRecord(record);
            }

            cursor = nextCursor;
        } while (cursor != null);

        if (totalRows > 0)
        {
            logger.LogInformation("  {table}: {count} rows", tableName, totalRows);
        }

        return totalRows;
    }

}
