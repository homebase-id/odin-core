using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.System;
using Odin.Core.Time;

#nullable enable

namespace Odin.Core.Storage.DatabaseImport;

// SEB:NOTE all this stuff in here is experimental and NOT production ready

/*

Intermittent bug:

$ rm -r $HOME/tmp/dotyou
Run dev backend in sqlite mode and local payload storage. It correctly creates the 4 dev hobbits.
Login in FE with Frodo and Sam. Connect them. Send af few chat messages between them.
Stop backend.

$ mv $HOME/tmp/dotyou $HOME/tmp/dotyou-backup

Clean PG database
Run dev backend in pg mode and local payload storage. It correctly creates 0 dev hobbits.
System tables have now been created in PG.
Directory $HOME/tmp/dotyou has been created again (with no registrations)

$ cp -r $HOME/tmp/dotyou-backup/tenants/payloads/* $HOME/tmp/dotyou/tenants/payloads/

Run sqlite2pg-all command line program. The program imports all rows from system tables and all rows from each of the 4 hobbits.

Run dev backend in pg mode and local payload storage. It correctly loads the 4 dev hobbits that were just imported.

Login with Frodo in frontend. Error:

2026-04-08T12:24:44.7827820+02:00 ERR 1dc14583-c1de-4567-a7a4-98dae1db07f5 frodo.dotyou.cloud frodo.dotyou.cloud Failed to update local read time for file: FileId=16c9d619-d06b-b800-919c-ab10df6efba0 Drive=9ff813af-f2d6-1e2f-9b9d-b189e72d1a11 (Cannot update local app data for non-existent file)
Odin.Core.Exceptions.OdinClientException: Cannot update local app data for non-existent file
  at Odin.Services.Drives.FileSystem.Base.DriveStorageServiceBase.UpdateLocalReadTime(InternalDriveFileId file, IOdinContext odinContext, Nullable`1 timestamp) in /home/seb/code/odin/odin-core/src/services/Odin.Services/Drives/FileSystem/Base/DriveStorageServiceBase.cs:line 1278
  at Odin.Services.Peer.Outgoing.Drive.Transfer.PeerOutgoingTransferService.SendReadReceipt(List`1 files, IOdinContext odinContext, FileSystemType fileSystemType, Nullable`1 timestamp) in /home/seb/code/odin/odin-core/src/services/Odin.Services/Peer/Outgoing/Drive/Transfer/PeerOutgoingTransferService.cs:line 292

Problems:
- Why doesn't the file exist (it was explicitly copied above) ?
- Why is frodo sending a read-receipt at this time? All chat messages were accounted for.
 */

public static class DataImporter
{
    private const int PageSize = 100;

    public static async Task ImportIdentityAsync(
        ILogger logger,
        string identityDomain,
        SystemDatabase sourceSystemDatabase,
        SystemDatabase targetSystemDatabase,
        IdentityDatabase sourceIdentityDatabase,
        IdentityDatabase targetIdentityDatabase,
        bool commit)
    {
        logger.LogInformation("Importing identity database {identityDomain}", identityDomain);

        await using var systemTransaction = await targetSystemDatabase.BeginStackedTransactionAsync();
        await using var identityTransaction = await targetIdentityDatabase.BeginStackedTransactionAsync();

        var totalRows = 0;
        totalRows += await ImportSystemTablesForIdentityAsync(logger, identityDomain, sourceSystemDatabase, targetSystemDatabase);
        totalRows += await ImportIdentityTablesAsync(logger, sourceIdentityDatabase, targetIdentityDatabase);

        if (!commit)
        {
            logger.LogInformation("Dry run: rolling back {count} rows", totalRows);
        }
        else
        {
            logger.LogInformation("Imported {count} total rows", totalRows);
            systemTransaction.Commit();
            identityTransaction.Commit();
        }
    }

    //
    // Imports ALL system database tables (no per-identity filtering).
    // Use this when migrating an entire SQLite system database to PostgreSQL.
    //
    public static async Task ImportAllSystemDataAsync(
        ILogger logger,
        SystemDatabase sourceSystemDatabase,
        SystemDatabase targetSystemDatabase,
        bool commit)
    {
        logger.LogInformation("Importing all system database tables");

        // Refuse to import into a non-empty target. Registrations is the canonical signal
        // that the target has already had data imported into it; mixing a fresh import on
        // top of existing rows would silently merge two unrelated installations.
        var existingRegistrations = await targetSystemDatabase.Registrations.GetAllAsync();
        if (existingRegistrations.Count > 0)
        {
            throw new InvalidOperationException(
                $"Refusing to import: target system database already contains "
                + $"{existingRegistrations.Count} registration(s). The target must be empty.");
        }

        await using var systemTransaction = await targetSystemDatabase.BeginStackedTransactionAsync();

        var totalRows = 0;

        // Jobs
        totalRows += await ImportTableAsync(sourceSystemDatabase.Jobs.PagingByRowIdAsync,
            r => targetSystemDatabase.Jobs.InsertAsync(r), logger, sourceSystemDatabase.Jobs.TableName);

        // Certificates
        totalRows += await ImportTableAsync(sourceSystemDatabase.Certificates.PagingByRowIdAsync,
            r => targetSystemDatabase.Certificates.InsertAsync(r), logger, sourceSystemDatabase.Certificates.TableName);

        // LastSeen
        totalRows += await ImportTableAsync(sourceSystemDatabase.LastSeen.PagingByRowIdAsync,
            r => targetSystemDatabase.LastSeen.InsertAsync(r), logger, sourceSystemDatabase.LastSeen.TableName);

        // Registrations
        totalRows += await ImportTableAsync(sourceSystemDatabase.Registrations.PagingByRowIdAsync,
            r => targetSystemDatabase.Registrations.InsertAsync(r), logger, sourceSystemDatabase.Registrations.TableName);

        // Settings
        totalRows += await ImportTableAsync(sourceSystemDatabase.Settings.PagingByRowIdAsync,
            r => targetSystemDatabase.Settings.InsertAsync(r), logger, sourceSystemDatabase.Settings.TableName);

        if (!commit)
        {
            logger.LogInformation("Dry run: rolling back {count} system rows", totalRows);
        }
        else
        {
            logger.LogInformation("Imported {count} total system rows", totalRows);
            systemTransaction.Commit();
        }
    }

    //
    // Imports a single identity's identity-database tables only (no system data).
    // Use this in combination with ImportAllSystemDataAsync when migrating every
    // identity from a SQLite source to a PostgreSQL target.
    //
    public static async Task ImportIdentityOnlyAsync(
        ILogger logger,
        string identityDomain,
        IdentityDatabase sourceIdentityDatabase,
        IdentityDatabase targetIdentityDatabase,
        bool commit)
    {
        logger.LogInformation("Importing identity database {identityDomain}", identityDomain);

        await using var identityTransaction = await targetIdentityDatabase.BeginStackedTransactionAsync();

        var totalRows = await ImportIdentityTablesAsync(
            logger, sourceIdentityDatabase, targetIdentityDatabase);

        if (!commit)
        {
            logger.LogInformation("Dry run: rolling back {count} rows for {identityDomain}", totalRows, identityDomain);
        }
        else
        {
            logger.LogInformation("Imported {count} rows for {identityDomain}", totalRows, identityDomain);
            identityTransaction.Commit();
        }
    }

    //
    // Removes a single identity's footprint from the system database (Registrations + Certificates).
    // Use this to undo the system-level rows that ImportAllSystemDataAsync committed for an
    // identity whose subsequent ImportIdentityOnlyAsync failed — restoring the precondition the
    // singular sqlite2pg-identity command needs to retry that identity.
    //
    // Both rows are deleted: the singular ImportIdentityAsync bailout checks Registrations, and
    // ImportSystemTablesForIdentityAsync would later re-insert Certificates (which would conflict
    // with a leftover row from the failed ImportAllAsync).
    //
    public static async Task DeleteIdentityFromSystemDataAsync(
        ILogger logger,
        SystemDatabase targetSystemDatabase,
        Guid identityId,
        string identityDomain)
    {
        logger.LogInformation("Cleaning up system rows for {identityDomain}", identityDomain);

        await using var systemTransaction = await targetSystemDatabase.BeginStackedTransactionAsync();

        var registrationRows = await targetSystemDatabase.Registrations.DeleteAsync(identityId);
        var certificateRows = await targetSystemDatabase.Certificates.DeleteAsync(new OdinId(identityDomain));

        logger.LogInformation(
            "Deleted {registrations} registration row(s) and {certificates} certificate row(s) for {identityDomain}",
            registrationRows, certificateRows, identityDomain);

        systemTransaction.Commit();
    }

    //

    private static async Task<int> ImportSystemTablesForIdentityAsync(
        ILogger logger,
        string identityDomain,
        SystemDatabase sourceSystemDatabase,
        SystemDatabase targetSystemDatabase)
    {
        var totalRows = 0;

        // Registrations (only the row for this identity)
        totalRows += await ImportTableAsync(sourceSystemDatabase.Registrations.PagingByRowIdAsync,
            async r =>
            {
                if (r.primaryDomainName.Equals(identityDomain, StringComparison.OrdinalIgnoreCase))
                {
                    return await targetSystemDatabase.Registrations.InsertAsync(r);
                }
                return 0;
            }, logger, sourceSystemDatabase.Registrations.TableName);

        // Certificates (only the row for this identity)
        totalRows += await ImportTableAsync(sourceSystemDatabase.Certificates.PagingByRowIdAsync,
            async r =>
            {
                if (r.domain.DomainName.Equals(identityDomain, StringComparison.OrdinalIgnoreCase))
                {
                    return await targetSystemDatabase.Certificates.InsertAsync(r);
                }
                return 0;
            }, logger, sourceSystemDatabase.Certificates.TableName);

        return totalRows;
    }

    //

    private static async Task<int> ImportIdentityTablesAsync(
        ILogger logger,
        IdentityDatabase sourceIdentityDatabase,
        IdentityDatabase targetIdentityDatabase)
    {
        var totalRows = 0;

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

        return totalRows;
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
