using System;
using System.Collections.Generic;
using System.Linq;
using System.Data;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;
using Odin.Core.Storage.Database.Identity.Table;

[assembly: InternalsVisibleTo("Odin.Core.Storage.Tests")]

namespace Odin.Core.Storage.Database.Identity.Abstractions
{
    public class MainIndexMeta(
        ScopedIdentityConnectionFactory scopedConnectionFactory,
        OdinIdentity odinIdentity,
        TableDriveAclIndex driveAclIndex,
        TableDriveTagIndex driveTagIndex,
        TableDriveLocalTagIndex driveLocalTagIndex,
        TableDriveMainIndex driveMainIndex,
        TableDriveReactions driveReactions,
        TableDriveTransferHistory driveTransferHistory)
    {
        private readonly DatabaseType _databaseType = scopedConnectionFactory.DatabaseType;
        public readonly TableDriveLocalTagIndex DriveLocalTagIndex = driveLocalTagIndex;

        /// <summary>
        /// Deletes the file's rows from every per-file table.  With <paramref name="expectedVersionTag"/>, only while the
        /// header is still at that tag: otherwise nothing is deleted and 0 is returned.
        /// </summary>
        internal async Task<int> DeleteEntryAsync(Guid driveId, Guid fileId, Guid? expectedVersionTag = null)
        {
            await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var tx = await cn.BeginStackedTransactionAsync();

            // The main row first: it is the one the version tag guards, and a miss must leave the other tables alone
            var n = expectedVersionTag.HasValue
                ? await driveMainIndex.DeleteIfVersionTagAsync(driveId, fileId, expectedVersionTag.Value)
                : await driveMainIndex.DeleteAsync(driveId, fileId);
            if (n == 0 && expectedVersionTag.HasValue)
            {
                // Nothing was written; committing (not abandoning) the stacked transaction keeps an outer one usable
                tx.Commit();
                return 0;
            }

            await driveAclIndex.DeleteAllRowsAsync(driveId, fileId);
            await driveTagIndex.DeleteAllRowsAsync(driveId, fileId);
            await DriveLocalTagIndex.DeleteAllRowsAsync(driveId, fileId);
            await driveReactions.DeleteAllForPostAsync(driveId, fileId);
            await driveTransferHistory.DeleteAllRowsAsync(driveId, fileId);

            tx.Commit();
            return n;
        }

        /// <summary>
        /// Tables holding one row set per file, with the column naming the file. Reactions name it as the post.
        /// </summary>
        internal static readonly (string Table, string FileColumn)[] PerFileTables =
        [
            ("DriveMainIndex", "fileId"),
            ("DriveAclIndex", "fileId"),
            ("DriveTagIndex", "fileId"),
            ("DriveLocalTagIndex", "fileId"),
            ("DriveReactions", "postId"),
            ("DriveTransferHistory", "fileId"),
            ("Outbox", "fileId")
        ];

        /// <summary>
        /// Every table that holds a drive's files, keyed by driveId -- the per-file tables, swept whole. The Inbox is
        /// not here: it has its own cache (<c>TableInboxCached.DeleteBoxAsync</c>).
        /// </summary>
        internal static readonly string[] DriveContentTables = PerFileTables.Select(t => t.Table).ToArray();

        /// <summary>The upper bound <see cref="CountDriveFilesAsync"/> counts to; anything more is reported as this.</summary>
        internal const long FileCountCap = 10_001;

        /// <summary>
        /// Up to <paramref name="limit"/> of a drive's file ids, for purging in batches. With
        /// <paramref name="createdAtOrBefore"/>, only files created by then: emptying a drive spares what was
        /// uploaded after the owner asked.
        /// </summary>
        internal async Task<List<Guid>> GetDriveFileIdsAsync(Guid driveId, int limit, long? createdAtOrBefore)
        {
            await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var cmd = DriveFilesCommand(cn, "SELECT fileId", driveId, createdAtOrBefore, limit);

            var fileIds = new List<Guid>();
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                fileIds.Add(new Guid((byte[])rdr[0]));
            }

            return fileIds;
        }

        /// <summary>
        /// How many files <see cref="GetDriveFileIdsAsync"/> would still find, for purge progress -- counted only up to
        /// <see cref="FileCountCap"/>, so a poll never scans a drive of millions.
        /// </summary>
        internal async Task<long> CountDriveFilesAsync(Guid driveId, long? createdAtOrBefore)
        {
            await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var inner = DriveFilesCommand(cn, "SELECT 1", driveId, createdAtOrBefore, FileCountCap);
            inner.CommandText = $"SELECT COUNT(*) FROM ({inner.CommandText.TrimEnd(';')}) AS capped;";
            return Convert.ToInt64(await inner.ExecuteScalarAsync());
        }

        private ICommandWrapper DriveFilesCommand(IConnectionWrapper cn, string select, Guid driveId,
            long? createdAtOrBefore, long limit)
        {
            var cmd = cn.CreateCommand();
            cmd.CommandText = $"{select} FROM DriveMainIndex WHERE identityId = @identityId AND driveId = @driveId" +
                              (createdAtOrBefore.HasValue ? " AND created <= @createdAtOrBefore" : "") +
                              " LIMIT @limit;";
            cmd.AddParameter("@identityId", DbType.Binary, odinIdentity.IdentityId);
            cmd.AddParameter("@driveId", DbType.Binary, driveId);
            cmd.AddParameter("@limit", DbType.Int64, limit);
            if (createdAtOrBefore.HasValue)
            {
                cmd.AddParameter("@createdAtOrBefore", DbType.Int64, createdAtOrBefore.Value);
            }

            return cmd;
        }

        /// <summary>
        /// Deletes the listed files of a drive with their index, reaction, transfer-history and outbox rows, in one
        /// transaction. Payloads are the caller's to remove, first.
        /// </summary>
        internal async Task<long> DeleteFilesAsync(Guid driveId, IReadOnlyList<Guid> fileIds)
        {
            if (fileIds.Count == 0)
            {
                return 0;
            }

            await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var tx = await cn.BeginStackedTransactionAsync();

            var fileParams = string.Join(",", fileIds.Select((_, i) => $"@f{i}"));
            long n = 0;
            foreach (var (table, fileColumn) in PerFileTables)
            {
                await using var cmd = cn.CreateCommand();
                cmd.CommandText =
                    $"DELETE FROM {table} WHERE identityId = @identityId AND driveId = @driveId AND {fileColumn} IN ({fileParams});";
                cmd.AddParameter("@identityId", DbType.Binary, odinIdentity.IdentityId);
                cmd.AddParameter("@driveId", DbType.Binary, driveId);
                for (var i = 0; i < fileIds.Count; i++)
                {
                    cmd.AddParameter($"@f{i}", DbType.Binary, fileIds[i]);
                }

                n += await cmd.ExecuteNonQueryAsync();
            }

            tx.Commit();
            return n;
        }

        /// <summary>
        /// Deletes every file of a drive, with its index, reaction, transfer-history and outbox rows, in
        /// one transaction. The drive itself stays. Payloads are the caller's to remove.
        /// </summary>
        internal async Task<long> DeleteDriveContentAsync(Guid driveId)
        {
            await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var tx = await cn.BeginStackedTransactionAsync();

            long n = 0;
            foreach (var table in DriveContentTables)
            {
                await using var cmd = cn.CreateCommand();
                cmd.CommandText = $"DELETE FROM {table} WHERE identityId = @identityId AND driveId = @driveId;";
                cmd.AddParameter("@identityId", DbType.Binary, odinIdentity.IdentityId);
                cmd.AddParameter("@driveId", DbType.Binary, driveId);
                n += await cmd.ExecuteNonQueryAsync();
            }

            tx.Commit();
            return n;
        }

        internal async Task UpdateLocalTagsAsync(Guid driveId, Guid fileId, List<Guid> tags)
        {
            await DriveLocalTagIndex.UpdateLocalTagsAsync(driveId, fileId, tags);
        }

        /// <summary>
        /// Writes a file's share rows (docs/selective-share-pr3-plan.md, section 3), in one transaction with the file's row
        /// locked, so a hard delete alongside can't leave a share behind. <paramref name="checkFile"/> sees the locked row
        /// first: it may refuse by throwing, and may return a new required group with the header's server data to match
        /// (the first share lifts an Owner file to Connected). The file's modified moves and its version tag stays.
        /// Returns the file's row as it was read, or null, writing nothing, when there is no file.
        /// </summary>
        internal async Task<DriveMainIndexRecord> WriteSharesAsync(Guid driveId, Guid fileId, IEnumerable<DriveAclIndexRecord> shares,
            Func<DriveMainIndexRecord, (int requiredSecurityGroup, string hdrServerData)?> checkFile)
        {
            await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var tx = await cn.BeginStackedTransactionAsync();

            var file = await driveMainIndex.GetLockedAsync(driveId, fileId);
            if (file == null)
            {
                tx.Commit();
                return null;
            }

            var group = checkFile(file);
            foreach (var share in shares)
            {
                share.driveId = driveId;
                share.fileId = fileId;
                await driveAclIndex.UpsertAsync(share);
            }

            await driveMainIndex.TouchKeepingVersionTagAsync(driveId, fileId, group?.requiredSecurityGroup, group?.hdrServerData);
            tx.Commit();
            return file;
        }

        /// <summary>
        /// By design does NOT update the TransferHistory and ReactionSummary fields, even when 
        /// they are specified in the record.
        /// </summary>
        /// <param name="driveMainIndexRecord"></param>
        /// <param name="accessControlList"></param>
        /// <param name="tagIdList"></param>
        /// <param name="useThisNewVersionTag"></param>
        /// <param name="reconcileAcl">False leaves the file's ACL rows as they are, whatever
        /// <paramref name="accessControlList"/> says: on a Selective Share drive the rows are the shares, written on
        /// their own (docs/selective-share-pr3-plan.md, section 1).</param>
        /// <returns></returns>
        internal async Task<int> BaseUpsertEntryZapZapAsync(DriveMainIndexRecord driveMainIndexRecord,
            List<Guid> accessControlList = null,
            List<Guid> tagIdList = null,
            Guid? useThisNewVersionTag = null,
            bool reconcileAcl = true)
        {
            driveMainIndexRecord.identityId = odinIdentity;

            await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var tx = await cn.BeginStackedTransactionAsync();

            var n = 0;
            n = await driveMainIndex.UpsertAllButReactionsAndTransferAsync(driveMainIndexRecord, useThisNewVersionTag);

            // The ACL rows are reconciled, not rewritten: a row whose circle or identity stays keeps what it holds
            // (docs/selective-share-plan.md). The tags hold nothing else, so they are simply rewritten.
            if (reconcileAcl)
            {
                await driveAclIndex.ReconcileRowsAsync(driveMainIndexRecord.driveId, driveMainIndexRecord.fileId, accessControlList);
            }

            await driveTagIndex.DeleteAllRowsAsync(driveMainIndexRecord.driveId, driveMainIndexRecord.fileId);
            await driveTagIndex.InsertRowsAsync(driveMainIndexRecord.driveId, driveMainIndexRecord.fileId, tagIdList);

            tx.Commit();

            return n;
        }


        //
        // THESE ARE HERE FOR LEGACY REASONS FOR TESTING JUST BECAUSE I'M
        // TOO LAZY TO REWRITE THE TESTS
        //

        /// <summary>
        /// Only kept to not change all tests! Do not use.
        /// </summary>
        internal async Task<(UnixTimeUtc created, UnixTimeUtc modified)> TestAddEntryPassalongToUpsertAsync(Guid driveId, Guid fileId,
            Guid? globalTransitId,
            Int32 fileType,
            Int32 dataType,
            string senderId,
            Guid? groupId,
            Guid? uniqueId,
            Int32 archivalStatus,
            UnixTimeUtc userDate,
            Int32 requiredSecurityGroup,
            List<Guid> accessControlList,
            List<Guid> tagIdList,
            Int64 byteCount,
            Int32 fileSystemType = (int)FileSystemType.Standard,
            Int32 fileState = 0)
        {
            if (byteCount < 1)
                throw new ArgumentException("byteCount must be at least 1");

            var r = new DriveMainIndexRecord()
            {
                driveId = driveId,
                fileId = fileId,
                globalTransitId = globalTransitId,
                fileState = fileState,
                userDate = userDate,
                fileType = fileType,
                dataType = dataType,
                senderId = senderId,
                groupId = groupId,
                uniqueId = uniqueId,
                archivalStatus = archivalStatus,
                historyStatus = 0,
                requiredSecurityGroup = requiredSecurityGroup,
                fileSystemType = fileSystemType,
                byteCount = byteCount,
                hdrEncryptedKeyHeader = """{"guid1": "123e4567-e89b-12d3-a456-426614174000", "guid2": "987f6543-e21c-45d6-b789-123456789abc"}""",
                hdrVersionTag = SequentialGuid.CreateGuid(),
                hdrAppData = """{"myAppData": "123e4567-e89b-12d3-a456-426614174000"}""",
                hdrReactionSummary = """{"reactionSummary": "123e4567-e89b-12d3-a456-426614174000"}""",
                hdrServerData = """ {"serverData": "123e4567-e89b-12d3-a456-426614174000"}""",
                hdrTransferHistory = """{"TransferStatus": "123e4567-e89b-12d3-a456-426614174000"}""",
                hdrFileMetaData = """{"fileMetaData": "123e4567-e89b-12d3-a456-426614174000"}""",
                hdrTmpDriveAlias = SequentialGuid.CreateGuid(),
                hdrTmpDriveType = SequentialGuid.CreateGuid()
            };
            await BaseUpsertEntryZapZapAsync(r, accessControlList: accessControlList, tagIdList: tagIdList);

            return (r.created, r.modified);
        }
    }
}