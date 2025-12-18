using System;
using System.Collections.Generic;
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
        TableDriveMainIndex driveMainIndex)
    {
        private readonly DatabaseType _databaseType = scopedConnectionFactory.DatabaseType;
        public readonly TableDriveLocalTagIndex DriveLocalTagIndex = driveLocalTagIndex;

        internal async Task<int> DeleteEntryAsync(Guid driveId, Guid fileId)
        {
            await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var tx = await cn.BeginStackedTransactionAsync();

            var n = 0;
            await driveAclIndex.DeleteAllRowsAsync(driveId, fileId);
            await driveTagIndex.DeleteAllRowsAsync(driveId, fileId);
            n = await driveMainIndex.DeleteAsync(driveId, fileId);

            tx.Commit();
            return n;
        }

        internal async Task UpdateLocalTagsAsync(Guid driveId, Guid fileId, List<Guid> tags)
        {
            await DriveLocalTagIndex.UpdateLocalTagsAsync(driveId, fileId, tags);
        }

        /// <summary>
        /// By design does NOT update the TransferHistory and ReactionSummary fields, even when 
        /// they are specified in the record.
        /// </summary>
        /// <param name="driveMainIndexRecord"></param>
        /// <param name="accessControlList"></param>
        /// <param name="tagIdList"></param>
        /// <param name="useThisNewVersionTag"></param>
        /// <returns></returns>
        internal async Task<int> BaseUpsertEntryZapZapAsync(DriveMainIndexRecord driveMainIndexRecord,
            List<Guid> accessControlList = null,
            List<Guid> tagIdList = null,
            Guid? useThisNewVersionTag = null)
        {
            driveMainIndexRecord.identityId = odinIdentity;

            await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var tx = await cn.BeginStackedTransactionAsync();

            var n = 0;
            n = await driveMainIndex.UpsertAllButReactionsAndTransferAsync(driveMainIndexRecord, useThisNewVersionTag);

            await driveAclIndex.DeleteAllRowsAsync(driveMainIndexRecord.driveId, driveMainIndexRecord.fileId);
            await driveAclIndex.InsertRowsAsync(driveMainIndexRecord.driveId, driveMainIndexRecord.fileId, accessControlList);
            await driveTagIndex.DeleteAllRowsAsync(driveMainIndexRecord.driveId, driveMainIndexRecord.fileId);
            await driveTagIndex.InsertRowsAsync(driveMainIndexRecord.driveId, driveMainIndexRecord.fileId, tagIdList);

            // NEXT: figure out if we want "addACL, delACL" and "addTags", "delTags" rather than always deleting them
            //

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