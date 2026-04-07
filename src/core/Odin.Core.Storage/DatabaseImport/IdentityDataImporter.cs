using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.System;
using Odin.Core.Time;

#nullable enable

namespace Odin.Core.Storage.DatabaseImport;

// SEB:NOTE all this stuff in here is experimental and NOT production ready

public static class IdentityDataImporter
{
    private const int PageSize = 100;
    public static async Task ImportAsync(
        ILogger logger,
        string identityDomain,
        SystemDatabase sourceSystemDatabase,
        SystemDatabase targetSystemDatabase,
        IdentityDatabase sourceIdentityDatabase,
        IdentityDatabase targetIdentityDatabase,
        bool dryRun)
    {
        await using var systemTransaction = await targetSystemDatabase.BeginStackedTransactionAsync();
        await using var identityTransaction = await targetIdentityDatabase.BeginStackedTransactionAsync();

        var totalRows = 0;

        //
        // System tables
        //


        //
        // Identity tables
        //

        // Drives
        totalRows += await ImportTableAsync(sourceIdentityDatabase.Drives.PagingByRowIdAsync,
            r => targetIdentityDatabase.Drives.InsertAsync(r), logger, sourceIdentityDatabase.Drives.TableName);

        // DriveMainIndex
        totalRows += await ImportTableAsync(sourceIdentityDatabase.DriveMainIndex.PagingByRowIdAsync,
            r => targetIdentityDatabase.DriveMainIndex.InsertAsync(r), logger, sourceIdentityDatabase.DriveMainIndex.TableName);

        // DriveTransferHistory
        totalRows += await ImportTableAsync(sourceIdentityDatabase.DriveTransferHistory.PagingByRowIdAsync,
            r => targetIdentityDatabase.DriveTransferHistory.InsertAsync(r), logger, sourceIdentityDatabase.DriveTransferHistory.TableName);

        // DriveAclIndex
        totalRows += await ImportTableAsync(sourceIdentityDatabase.DriveAclIndex.PagingByRowIdAsync,
            r => targetIdentityDatabase.DriveAclIndex.InsertAsync(r), logger, sourceIdentityDatabase.DriveAclIndex.TableName);

        // DriveTagIndex
        totalRows += await ImportTableAsync(sourceIdentityDatabase.DriveTagIndex.PagingByRowIdAsync,
            r => targetIdentityDatabase.DriveTagIndex.InsertAsync(r), logger, sourceIdentityDatabase.DriveTagIndex.TableName);

        // DriveLocalTagIndex
        totalRows += await ImportTableAsync(sourceIdentityDatabase.DriveLocalTagIndex.PagingByRowIdAsync,
            r => targetIdentityDatabase.DriveLocalTagIndex.InsertAsync(r), logger, sourceIdentityDatabase.DriveLocalTagIndex.TableName);

        // DriveReactions
        totalRows += await ImportTableAsync(sourceIdentityDatabase.DriveReactions.PagingByRowIdAsync,
            r => targetIdentityDatabase.DriveReactions.InsertAsync(r), logger, sourceIdentityDatabase.DriveReactions.TableName);

        // AppNotifications
        totalRows += await ImportTableAsync(sourceIdentityDatabase.AppNotifications.PagingByRowIdAsync,
            r => targetIdentityDatabase.AppNotifications.InsertAsync(r), logger, sourceIdentityDatabase.AppNotifications.TableName);

        // ClientRegistrations
        totalRows += await ImportTableAsync(sourceIdentityDatabase.ClientRegistrations.PagingByRowIdAsync,
            r => targetIdentityDatabase.ClientRegistrations.InsertAsync(r), logger, sourceIdentityDatabase.ClientRegistrations.TableName);

        // Circle
        totalRows += await ImportTableAsync(sourceIdentityDatabase.Circle.PagingByRowIdAsync,
            r => targetIdentityDatabase.Circle.InsertAsync(r), logger, sourceIdentityDatabase.Circle.TableName);

        // CircleMember
        totalRows += await ImportTableAsync(sourceIdentityDatabase.CircleMember.PagingByRowIdAsync,
            r => targetIdentityDatabase.CircleMember.InsertAsync(r), logger, sourceIdentityDatabase.CircleMember.TableName);

        // Connections
        totalRows += await ImportTableAsync(sourceIdentityDatabase.Connections.PagingByRowIdAsync,
            r => targetIdentityDatabase.Connections.InsertAsync(r), logger, sourceIdentityDatabase.Connections.TableName);

        // AppGrants
        totalRows += await ImportTableAsync(sourceIdentityDatabase.AppGrants.PagingByRowIdAsync,
            r => targetIdentityDatabase.AppGrants.InsertAsync(r), logger, sourceIdentityDatabase.AppGrants.TableName);

        // ImFollowing
        totalRows += await ImportTableAsync(sourceIdentityDatabase.ImFollowing.PagingByRowIdAsync,
            r => targetIdentityDatabase.ImFollowing.InsertAsync(r), logger, sourceIdentityDatabase.ImFollowing.TableName);

        // FollowsMe
        totalRows += await ImportTableAsync(sourceIdentityDatabase.FollowsMe.PagingByRowIdAsync,
            r => targetIdentityDatabase.FollowsMe.InsertAsync(r), logger, sourceIdentityDatabase.FollowsMe.TableName);

        // Inbox
        totalRows += await ImportTableAsync(sourceIdentityDatabase.Inbox.PagingByRowIdAsync,
            r => targetIdentityDatabase.Inbox.InsertAsync(r), logger, sourceIdentityDatabase.Inbox.TableName);

        // Outbox
        totalRows += await ImportTableAsync(sourceIdentityDatabase.Outbox.PagingByRowIdAsync,
            r => targetIdentityDatabase.Outbox.InsertAsync(r), logger, sourceIdentityDatabase.Outbox.TableName);

        // KeyValue
        totalRows += await ImportTableAsync(sourceIdentityDatabase.KeyValue.PagingByRowIdAsync,
            r => targetIdentityDatabase.KeyValue.InsertAsync(r), logger, sourceIdentityDatabase.KeyValue.TableName);

        // KeyTwoValue
        totalRows += await ImportTableAsync(sourceIdentityDatabase.KeyTwoValue.PagingByRowIdAsync,
            r => targetIdentityDatabase.KeyTwoValue.InsertAsync(r), logger, sourceIdentityDatabase.KeyTwoValue.TableName);

        // KeyThreeValue
        totalRows += await ImportTableAsync(sourceIdentityDatabase.KeyThreeValue.PagingByRowIdAsync,
            r => targetIdentityDatabase.KeyThreeValue.InsertAsync(r), logger, sourceIdentityDatabase.KeyThreeValue.TableName);

        // KeyUniqueThreeValue
        totalRows += await ImportTableAsync(sourceIdentityDatabase.KeyUniqueThreeValue.PagingByRowIdAsync,
            r => targetIdentityDatabase.KeyUniqueThreeValue.InsertAsync(r), logger, sourceIdentityDatabase.KeyUniqueThreeValue.TableName);

        // Nonce (skip expired records since InsertAsync rejects them)
        totalRows += await ImportTableAsync(sourceIdentityDatabase.Nonce.PagingByRowIdAsync,
            async r =>
            {
                if (r.expiration > UnixTimeUtc.Now())
                {
                    return await targetIdentityDatabase.Nonce.InsertAsync(r);
                }
                return 0;
            }, logger, sourceIdentityDatabase.Nonce.TableName);

        //
        // Commit ?
        //

        if (!dryRun)
        {
            logger.LogInformation("Imported {count} total rows", totalRows);
        }
        else
        {
            logger.LogInformation("Dry run: rolling back {count} rows", totalRows);
        }
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
