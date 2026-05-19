using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;

#nullable enable

namespace Odin.Core.Storage.DatabaseImport;

// SEB:NOTE this is the companion to DataImporter. It repairs the created/modified
// columns on rows that DataImporter already wrote to the PostgreSQL target.
//
// The CRUD InsertAsync methods all hard-code created/modified to NOW() (see
// {sqlNowStr} usage in every Table*CRUD.cs), so an import collapses every row's
// timestamps onto the import wall-clock time. We can't fix the importer
// retroactively without re-running it, so this patcher re-reads the SQLite
// source and UPDATEs the target row-by-row, keyed by each table's natural
// UNIQUE key.
//
// It refuses to overwrite any row whose target `modified` is past the supplied
// cutoff -- those rows have been touched by real activity since the import and
// must not be reverted.

public static class DataImportPatcher
{
    private const int PageSize = 100;

    // The moment legitimate post-import activity began. Rows in the target whose
    // `modified` is later than this value have been touched by real users and
    // must not be reverted by the patcher.
    public static readonly UnixTimeUtc DefaultCutoffUtc = new UnixTimeUtc(
        DateTimeOffset.Parse(
            "2026-05-19T05:25:00Z",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
            .ToUnixTimeMilliseconds());

    public static async Task PatchAllSystemDataAsync(
        ILogger logger,
        SystemDatabase sourceSystemDatabase,
        SystemDatabase targetSystemDatabase,
        UnixTimeUtc cutoff,
        bool commit)
    {
        logger.LogInformation(
            "Patching system tables (cutoff = {cutoffMs} ms / {cutoffIso})",
            cutoff.milliseconds,
            DateTimeOffset.FromUnixTimeMilliseconds(cutoff.milliseconds).ToString("O", CultureInfo.InvariantCulture));

        await using var tx = await targetSystemDatabase.BeginStackedTransactionAsync();
        await using var cn = await targetSystemDatabase.CreateScopedConnectionAsync();

        long totalPatched = 0;

        totalPatched += (await PatchTableAsync(
            sourceSystemDatabase.Jobs.PagingByRowIdAsync,
            cn, "Jobs",
            "id = @id",
            (cmd, r) => cmd.AddParameter("@id", DbType.Binary, r.id),
            r => (r.created.milliseconds, r.modified.milliseconds),
            cutoff, logger)).Patched;

        totalPatched += (await PatchTableAsync(
            sourceSystemDatabase.Certificates.PagingByRowIdAsync,
            cn, "Certificates",
            "domain = @domain",
            (cmd, r) => cmd.AddParameter("@domain", DbType.String, r.domain.DomainName),
            r => (r.created.milliseconds, r.modified.milliseconds),
            cutoff, logger)).Patched;

        totalPatched += (await PatchTableAsync(
            sourceSystemDatabase.Registrations.PagingByRowIdAsync,
            cn, "Registrations",
            "identityId = @identityId",
            (cmd, r) => cmd.AddParameter("@identityId", DbType.Binary, r.identityId),
            r => (r.created.milliseconds, r.modified.milliseconds),
            cutoff, logger)).Patched;

        totalPatched += (await PatchTableAsync(
            sourceSystemDatabase.Settings.PagingByRowIdAsync,
            cn, "Settings",
            "key = @key",
            (cmd, r) => cmd.AddParameter("@key", DbType.String, r.key),
            r => (r.created.milliseconds, r.modified.milliseconds),
            cutoff, logger)).Patched;

        if (!commit)
        {
            logger.LogInformation("Dry run: rolling back {count} system patches", totalPatched);
        }
        else
        {
            logger.LogInformation("Patched {count} total system rows", totalPatched);
            tx.Commit();
        }
    }

    public static async Task PatchIdentityOnlyAsync(
        ILogger logger,
        string identityDomain,
        IdentityDatabase sourceIdentityDatabase,
        IdentityDatabase targetIdentityDatabase,
        UnixTimeUtc cutoff,
        bool commit)
    {
        logger.LogInformation(
            "Patching identity database {identityDomain} (cutoff = {cutoffMs} ms)",
            identityDomain, cutoff.milliseconds);

        await using var tx = await targetIdentityDatabase.BeginStackedTransactionAsync();
        await using var cn = await targetIdentityDatabase.CreateScopedConnectionAsync();

        long totalPatched = 0;

        totalPatched += (await PatchTableAsync(
            sourceIdentityDatabase.Drives.PagingByRowIdAsync,
            cn, "Drives",
            "identityId = @identityId AND DriveId = @DriveId",
            (cmd, r) =>
            {
                cmd.AddParameter("@identityId", DbType.Binary, r.identityId);
                cmd.AddParameter("@DriveId", DbType.Binary, r.DriveId);
            },
            r => (r.created.milliseconds, r.modified.milliseconds),
            cutoff, logger)).Patched;

        totalPatched += (await PatchTableAsync(
            sourceIdentityDatabase.DriveMainIndex.PagingByRowIdAsync,
            cn, "DriveMainIndex",
            "identityId = @identityId AND driveId = @driveId AND fileId = @fileId",
            (cmd, r) =>
            {
                cmd.AddParameter("@identityId", DbType.Binary, r.identityId);
                cmd.AddParameter("@driveId", DbType.Binary, r.driveId);
                cmd.AddParameter("@fileId", DbType.Binary, r.fileId);
            },
            r => (r.created.milliseconds, r.modified.milliseconds),
            cutoff, logger)).Patched;

        totalPatched += (await PatchTableAsync(
            sourceIdentityDatabase.AppNotifications.PagingByRowIdAsync,
            cn, "AppNotifications",
            "identityId = @identityId AND notificationId = @notificationId",
            (cmd, r) =>
            {
                cmd.AddParameter("@identityId", DbType.Binary, r.identityId);
                cmd.AddParameter("@notificationId", DbType.Binary, r.notificationId);
            },
            r => (r.created.milliseconds, r.modified.milliseconds),
            cutoff, logger)).Patched;

        totalPatched += (await PatchTableAsync(
            sourceIdentityDatabase.ClientRegistrations.PagingByRowIdAsync,
            cn, "ClientRegistrations",
            "identityId = @identityId AND catId = @catId",
            (cmd, r) =>
            {
                cmd.AddParameter("@identityId", DbType.Binary, r.identityId);
                cmd.AddParameter("@catId", DbType.Binary, r.catId);
            },
            r => (r.created.milliseconds, r.modified.milliseconds),
            cutoff, logger)).Patched;

        totalPatched += (await PatchTableAsync(
            sourceIdentityDatabase.Connections.PagingByRowIdAsync,
            cn, "Connections",
            "identityId = @identityId AND identity = @identity",
            (cmd, r) =>
            {
                cmd.AddParameter("@identityId", DbType.Binary, r.identityId);
                cmd.AddParameter("@identity", DbType.String, r.identity.DomainName);
            },
            r => (r.created.milliseconds, r.modified.milliseconds),
            cutoff, logger)).Patched;

        totalPatched += (await PatchTableAsync(
            sourceIdentityDatabase.ImFollowing.PagingByRowIdAsync,
            cn, "ImFollowing",
            "identityId = @identityId AND identity = @identity AND driveId = @driveId",
            (cmd, r) =>
            {
                cmd.AddParameter("@identityId", DbType.Binary, r.identityId);
                cmd.AddParameter("@identity", DbType.String, r.identity.DomainName);
                cmd.AddParameter("@driveId", DbType.Binary, r.driveId);
            },
            r => (r.created.milliseconds, r.modified.milliseconds),
            cutoff, logger)).Patched;

        totalPatched += (await PatchTableAsync(
            sourceIdentityDatabase.FollowsMe.PagingByRowIdAsync,
            cn, "FollowsMe",
            "identityId = @identityId AND identity = @identity AND driveId = @driveId",
            (cmd, r) =>
            {
                cmd.AddParameter("@identityId", DbType.Binary, r.identityId);
                cmd.AddParameter("@identity", DbType.String, r.identity);
                cmd.AddParameter("@driveId", DbType.Binary, r.driveId);
            },
            r => (r.created.milliseconds, r.modified.milliseconds),
            cutoff, logger)).Patched;

        totalPatched += (await PatchTableAsync(
            sourceIdentityDatabase.Inbox.PagingByRowIdAsync,
            cn, "Inbox",
            "identityId = @identityId AND fileId = @fileId",
            (cmd, r) =>
            {
                cmd.AddParameter("@identityId", DbType.Binary, r.identityId);
                cmd.AddParameter("@fileId", DbType.Binary, r.fileId);
            },
            r => (r.created.milliseconds, r.modified.milliseconds),
            cutoff, logger)).Patched;

        totalPatched += (await PatchTableAsync(
            sourceIdentityDatabase.Outbox.PagingByRowIdAsync,
            cn, "Outbox",
            "identityId = @identityId AND driveId = @driveId AND fileId = @fileId AND recipient = @recipient",
            (cmd, r) =>
            {
                cmd.AddParameter("@identityId", DbType.Binary, r.identityId);
                cmd.AddParameter("@driveId", DbType.Binary, r.driveId);
                cmd.AddParameter("@fileId", DbType.Binary, r.fileId);
                cmd.AddParameter("@recipient", DbType.String, r.recipient);
            },
            r => (r.created.milliseconds, r.modified.milliseconds),
            cutoff, logger)).Patched;

        totalPatched += (await PatchTableAsync(
            sourceIdentityDatabase.Nonce.PagingByRowIdAsync,
            cn, "Nonce",
            "identityId = @identityId AND id = @id",
            (cmd, r) =>
            {
                cmd.AddParameter("@identityId", DbType.Binary, r.identityId);
                cmd.AddParameter("@id", DbType.Binary, r.id);
            },
            r => (r.created.milliseconds, r.modified.milliseconds),
            cutoff, logger)).Patched;

        if (!commit)
        {
            logger.LogInformation(
                "Dry run: rolling back {count} patches for {identityDomain}", totalPatched, identityDomain);
        }
        else
        {
            logger.LogInformation(
                "Patched {count} rows for {identityDomain}", totalPatched, identityDomain);
            tx.Commit();
        }
    }

    //

    private readonly struct TableStats
    {
        public long Seen { get; init; }
        public long Patched { get; init; }
    }

    private static async Task<TableStats> PatchTableAsync<TRecord>(
        Func<int, long?, Task<(List<TRecord>, long?)>> readSourcePage,
        IConnectionWrapper targetConnection,
        string tableName,
        string whereKey,
        Action<ICommandWrapper, TRecord> bindKeyParams,
        Func<TRecord, (long createdMs, long modifiedMs)> readTimestamps,
        UnixTimeUtc cutoff,
        ILogger logger)
    {
        // The cutoff guard lives in the WHERE clause: any row whose current
        // target `modified` is past the cutoff is silently skipped, because the
        // UPDATE matches zero rows. We don't distinguish "skipped past cutoff"
        // from "row missing in target" here -- callers can tell from a row count
        // diff against the source if they care.
        var sql =
            $"UPDATE {tableName} SET created = @__created, modified = @__modified " +
            $"WHERE ({whereKey}) AND modified <= @__cutoff";

        var seen = 0L;
        var patched = 0L;

        long? cursor = null;
        do
        {
            var (records, nextCursor) = await readSourcePage(PageSize, cursor);

            foreach (var record in records)
            {
                seen++;

                await using var cmd = targetConnection.CreateCommand();
                cmd.CommandText = sql;

                var (createdMs, modifiedMs) = readTimestamps(record);
                cmd.AddParameter("@__created", DbType.Int64, createdMs);
                cmd.AddParameter("@__modified", DbType.Int64, modifiedMs);
                cmd.AddParameter("@__cutoff", DbType.Int64, cutoff.milliseconds);
                bindKeyParams(cmd, record);

                if (await cmd.ExecuteNonQueryAsync() > 0)
                {
                    patched++;
                }
            }

            cursor = nextCursor;
        } while (cursor != null);

        if (seen > 0)
        {
            logger.LogInformation(
                "  {table}: seen={seen} patched={patched} skipped={skipped}",
                tableName, seen, patched, seen - patched);
        }

        return new TableStats { Seen = seen, Patched = patched };
    }
}
