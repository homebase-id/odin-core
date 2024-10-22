using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer.Encryption;
using QueryBatchCursor = Odin.Core.Storage.SQLite.IdentityDatabase.QueryBatchCursor;

namespace Odin.Services.Drives.DriveCore.Query.Sqlite;

public class SqliteDatabaseManager(TenantSystemStorage tenantSystemStorage, StorageDrive drive, ILogger<object> logger)
    : IDriveDatabaseManager
{
    private readonly IdentityDatabase _db = tenantSystemStorage.IdentityDatabase;

    public StorageDrive Drive { get; init; } = drive;

    public async Task<(long, IEnumerable<Guid>, bool hasMoreRows)> GetModifiedCoreAsync(IOdinContext odinContext, FileSystemType fileSystemType,
        FileQueryParams qp, QueryModifiedResultOptions options, IdentityDatabase db)
    {
        var callerContext = odinContext.Caller;

        var requiredSecurityGroup = new IntRange(0, (int)callerContext.SecurityLevel);
        var aclList = GetAcl(odinContext);
        var cursor = new UnixTimeUtcUnique(options.Cursor);

        // TODO TODD - use moreRows
        (var results, var moreRows, cursor) = await _db.metaIndex.QueryModifiedAsync(
            Drive.Id,
            noOfItems: options.MaxRecords,
            cursor,
            fileSystemType: (Int32)fileSystemType,
            stopAtModifiedUnixTimeSeconds: new UnixTimeUtcUnique(options.MaxDate),
            requiredSecurityGroup: requiredSecurityGroup,
            filetypesAnyOf: qp.FileType?.ToList(),
            datatypesAnyOf: qp.DataType?.ToList(),
            senderidAnyOf: qp.Sender?.ToList(),
            groupIdAnyOf: qp.GroupId?.ToList(),
            uniqueIdAnyOf: qp.ClientUniqueIdAtLeastOne?.ToList(),
            userdateSpan: qp.UserDate,
            aclAnyOf: aclList,
            tagsAnyOf: qp.TagsMatchAtLeastOne?.ToList(),
            tagsAllOf: qp.TagsMatchAll?.ToList(),
            archivalStatusAnyOf: qp.ArchivalStatus?.ToList());

        return (cursor.uniqueTime, results.AsEnumerable(), moreRows);
    }


    public async Task<(QueryBatchCursor, IEnumerable<Guid>, bool hasMoreRows)> GetBatchCoreAsync(IOdinContext odinContext,
        FileSystemType fileSystemType, FileQueryParams qp, QueryBatchResultOptions options, IdentityDatabase db)
    {
        var securityRange = new IntRange(0, (int)odinContext.Caller.SecurityLevel);
        var aclList = GetAcl(odinContext);
        var cursor = options.Cursor;

        if (options.Ordering == Ordering.Default)
        {
            (var results, var moreRows, cursor) = await _db.metaIndex.QueryBatchAutoAsync(
                Drive.Id,
                noOfItems: options.MaxRecords,
                cursor,
                fileStateAnyOf: qp.FileState?.Select(f => (int)f).ToList(),
                fileSystemType: (Int32)fileSystemType,
                requiredSecurityGroup: securityRange,
                globalTransitIdAnyOf: qp.GlobalTransitId?.ToList(),
                filetypesAnyOf: qp.FileType?.ToList(),
                datatypesAnyOf: qp.DataType?.ToList(),
                senderidAnyOf: qp.Sender?.ToList(),
                groupIdAnyOf: qp.GroupId?.Select(g => g).ToList(),
                userdateSpan: qp.UserDate,
                aclAnyOf: aclList?.ToList(),
                uniqueIdAnyOf: qp.ClientUniqueIdAtLeastOne?.ToList(),
                tagsAnyOf: qp.TagsMatchAtLeastOne?.ToList(),
                tagsAllOf: qp.TagsMatchAll?.ToList(),
                archivalStatusAnyOf: qp.ArchivalStatus?.ToList());

            return (cursor, results.Select(r => r), moreRows);
        }

        // if the caller was explicit in how they want results...
        return await GetBatchExplicitOrderingAsync(odinContext, fileSystemType, qp, options, db);
    }

    private List<Guid> GetAcl(IOdinContext odinContext)
    {
        var callerContext = odinContext.Caller;

        var aclList = new List<Guid>();
        if (callerContext.IsOwner == false)
        {
            if (!callerContext.IsAnonymous)
            {
                aclList.Add(odinContext.GetCallerOdinIdOrFail().ToHashId());
            }

            aclList.AddRange(callerContext.Circles?.Select(c => c.Value) ?? Array.Empty<Guid>());
        }

        return aclList.Any() ? aclList : null;
    }

    string GuidOneOrTwo(Guid? v1, Guid? v2)
    {
        string v1Str = v1.HasValue ? v1.ToString() : "NULL";
        string v2Str = v2.HasValue ? v2.ToString() : "NULL";

        if (v1 == v2)
            return "{" + v1Str + "}";
        else
            return "{" + v1Str + "," + v2Str + "}";
    }

    string IntOneOrTwo(int v1, int v2)
    {
        string v1Str = v1.ToString();
        string v2Str = v2.ToString();

        if (v1 == v2)
            return "{" + v1Str + "}";
        else
            return "{" + v1Str + "," + v2Str + "}";
    }

    public async Task SaveFileHeaderAsync(ServerFileHeader header, IdentityDatabase db)
    {
        var metadata = header.FileMetadata;

        int securityGroup = (int)header.ServerMetadata.AccessControlList.RequiredSecurityGroup;

        var acl = new List<Guid>();
        acl.AddRange(header.ServerMetadata.AccessControlList.GetRequiredCircles());
        var ids = header.ServerMetadata.AccessControlList.GetRequiredIdentities().Select(odinId =>
            ((OdinId)odinId).ToHashId()
        );
        acl.AddRange(ids.ToList());

        var tags = metadata.AppData.Tags?.ToList();

        // TODO: this is a hack to clean up FileMetadata before writing to db.
        // We should have separate classes for DB model and API model
        var strippedFileMetadata = OdinSystemSerializer.SlowDeepCloneObject(header.FileMetadata);
        strippedFileMetadata.AppData = null;
        strippedFileMetadata.ReactionPreview = null;
        strippedFileMetadata.VersionTag = null;

        // TODO: this is a hack to clean up ServerMetaData before writing to db.
        // We should have separate classes for DB model and API model
        var strippedServerMetadata = OdinSystemSerializer.SlowDeepCloneObject(header.ServerMetadata);
        strippedServerMetadata.TransferHistory = null;

        var driveMainIndexRecord = new DriveMainIndexRecord
        {
            identityId = default,
            driveId = Drive.Id,
            fileId = metadata.File.FileId,
            globalTransitId = metadata.GlobalTransitId,
            uniqueId = metadata.AppData.UniqueId,
            groupId = metadata.AppData.GroupId,

            senderId = metadata.SenderOdinId,

            fileType = metadata.AppData.FileType,
            dataType = metadata.AppData.DataType,

            archivalStatus = metadata.AppData.ArchivalStatus,
            historyStatus = 0,
            userDate = metadata.AppData.UserDate ?? UnixTimeUtc.ZeroTime,
            requiredSecurityGroup = securityGroup,

            fileState = (int)metadata.FileState,
            fileSystemType = (int)header.ServerMetadata.FileSystemType,
            byteCount = header.ServerMetadata.FileByteCount,

            hdrEncryptedKeyHeader = OdinSystemSerializer.Serialize(header.EncryptedKeyHeader),

            hdrFileMetaData = OdinSystemSerializer.Serialize(strippedFileMetadata),

            hdrVersionTag = header.FileMetadata.VersionTag.GetValueOrDefault(),
            hdrAppData = OdinSystemSerializer.Serialize(metadata.AppData),

            hdrServerData = OdinSystemSerializer.Serialize(strippedServerMetadata),

            //this is updated by the SaveReactionSummary method
            // hdrReactionSummary = OdinSystemSerializer.Serialize(header.FileMetadata.ReactionPreview),
            // this is handled by the SaveTransferHistory method
            // hdrTransferStatus = OdinSystemSerializer.Serialize(header.ServerMetadata.TransferHistory),

            hdrTmpDriveAlias = this.Drive.TargetDriveInfo.Alias,
            hdrTmpDriveType = this.Drive.TargetDriveInfo.Type,
        };

        if (driveMainIndexRecord.driveId == Guid.Empty || driveMainIndexRecord.fileId == Guid.Empty)
        {
            throw new OdinSystemException("DriveId and FileId must be a non-empty GUID");
        }

        try
        {
            await _db.metaIndex.BaseUpsertEntryZapZapAsync(driveMainIndexRecord, acl, tags);
        }
        catch (SqliteException e)
        {
            if (e.SqliteErrorCode == 19)
            {
                DriveMainIndexRecord rf = null;
                DriveMainIndexRecord ru = null;
                DriveMainIndexRecord rt = null;

                rf = await _db.tblDriveMainIndex.GetAsync(Drive.Id, metadata.File.FileId);
                if (metadata.AppData.UniqueId.HasValue)
                    ru = await _db.tblDriveMainIndex.GetByUniqueIdAsync(Drive.Id, metadata.AppData.UniqueId);
                if (metadata.GlobalTransitId.HasValue)
                    rt = await _db.tblDriveMainIndex.GetByGlobalTransitIdAsync(Drive.Id, metadata.GlobalTransitId);

                string s = "";
                DriveMainIndexRecord r = null;

                if (rf != null)
                {
                    s += " FileId";
                    r = rf;
                }

                if (rt != null)
                {
                    s += " GlobalTransitId";
                    r = rt;
                }

                if (ru != null)
                {
                    s += " UniqueId";
                    r = ru;
                }

                //
                // I wonder if we should test if the client UniqueId is in fact the culprit. 
                // 
                logger.LogDebug(
                    "SqliteErrorCode:19 (found: [{index}]) - UniqueId:{uid}.  GlobalTransitId:{gtid}.  DriveId:{driveId}.   FileState {fileState}.   FileSystemType {fileSystemType}.  FileId {fileId}.  DriveName {driveName}",
                    s,
                    GuidOneOrTwo(metadata.AppData.UniqueId, r?.uniqueId),
                    GuidOneOrTwo(metadata.GlobalTransitId, r?.globalTransitId),
                    GuidOneOrTwo(Drive.Id, r?.driveId),
                    IntOneOrTwo((int)metadata.FileState, r?.fileState ?? -1),
                    IntOneOrTwo((int)header.ServerMetadata.FileSystemType, r?.fileSystemType ?? -1),
                    GuidOneOrTwo(metadata.File.FileId, r.fileId),
                    Drive.Name);

                throw new OdinClientException($"UniqueId [{metadata.AppData.UniqueId}] not unique.", OdinClientErrorCode.ExistingFileWithUniqueId);
            }
        }
    }


    public async Task SaveTransferHistoryAsync(Guid fileId, RecipientTransferHistory history, IdentityDatabase db)
    {
        var json = OdinSystemSerializer.Serialize(history);
        await _db.tblDriveMainIndex.UpdateTransferHistoryAsync(Drive.Id, fileId, json);
    }

    public async Task SaveReactionSummary(Guid fileId, ReactionSummary summary, IdentityDatabase db)
    {
        var json = summary == null ? "" : OdinSystemSerializer.Serialize(summary);
        await _db.tblDriveMainIndex.UpdateReactionSummaryAsync(Drive.Id, fileId, json);
    }

    public async Task<ServerFileHeader> GetFileHeaderAsync(Guid fileId, FileSystemType fileSystemType)
    {
        var record = await _db.tblDriveMainIndex.GetAsync(this.Drive.Id, fileId);

        if (null == record || record.fileSystemType != (int)fileSystemType)
        {
            return null;
        }

        var header = new ServerFileHeader
        {
            EncryptedKeyHeader = OdinSystemSerializer.Deserialize<EncryptedKeyHeader>(record.hdrEncryptedKeyHeader),
            FileMetadata = OdinSystemSerializer.Deserialize<FileMetadata>(record.hdrFileMetaData),
            ServerMetadata = OdinSystemSerializer.Deserialize<ServerMetadata>(record.hdrServerData)
        };

        //Now overwrite with column specific values
        header.FileMetadata.VersionTag = record.hdrVersionTag;
        header.FileMetadata.AppData = OdinSystemSerializer.Deserialize<AppFileMetaData>(record.hdrAppData);
        header.FileMetadata.ReactionPreview = string.IsNullOrEmpty(record.hdrReactionSummary)
            ? null
            : OdinSystemSerializer.Deserialize<ReactionSummary>(record.hdrReactionSummary);
        header.ServerMetadata.TransferHistory = string.IsNullOrEmpty(record.hdrTransferHistory)
            ? null
            : OdinSystemSerializer.Deserialize<RecipientTransferHistory>(record.hdrTransferHistory);

        return header;
    }

    /// <summary>
    /// Soft deleting a file
    /// </summary>
    /// <param name="header"></param>
    /// <param name="db"></param>
    /// <returns></returns>
    public Task SoftDeleteFileHeader(ServerFileHeader header, IdentityDatabase db)
    {
        throw new NotImplementedException("No longer needed, this will be removed");
    }

    public async Task HardDeleteFileHeaderAsync(InternalDriveFileId file, IdentityDatabase db)
    {
        await _db.metaIndex.DeleteEntryAsync(Drive.Id, file.FileId);
    }

    public async Task LoadLatestIndexAsync(IdentityDatabase db)
    {
        await _db.CreateDatabaseAsync(false);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        // NO! Database is not owned by this class
        // _db.Commit();
        // _db.Dispose();
    }

    public async Task AddReactionAsync(OdinId odinId, Guid fileId, string reaction, IdentityDatabase db)
    {
        try
        {
            await _db.tblDriveReactions.InsertAsync(new DriveReactionsRecord()
            {
                driveId = Drive.Id,
                identity = odinId,
                postId = fileId,
                singleReaction = reaction
            });
        }
        catch (SqliteException e)
        {
            if (e.SqliteErrorCode == 19)
            {
                throw new OdinClientException("Cannot add duplicate reaction");
            }

            throw;
        }
    }

    public async Task DeleteReactionsAsync(OdinId odinId, Guid fileId, IdentityDatabase db)
    {
        await _db.tblDriveReactions.DeleteAllReactionsAsync(Drive.Id, odinId, fileId);
    }

    public async Task DeleteReactionAsync(OdinId odinId, Guid fileId, string reaction, IdentityDatabase db)
    {
        await _db.tblDriveReactions.DeleteAsync(Drive.Id, odinId, fileId, reaction);
    }

    public async Task<(List<string>, int)> GetReactionsAsync(Guid fileId, IdentityDatabase db)
    {
        return await _db.tblDriveReactions.GetPostReactionsAsync(Drive.Id, fileId);
    }

    public async Task<(List<ReactionCount> reactions, int total)> GetReactionSummaryByFileAsync(Guid fileId, IdentityDatabase db)
    {
        var (reactionContentList, countByReactionsList, total) = await _db.tblDriveReactions.GetPostReactionsWithDetailsAsync(Drive.Id, fileId);

        var results = new List<ReactionCount>();

        for (int i = 0; i < reactionContentList.Count; i++)
        {
            results.Add(new ReactionCount()
            {
                ReactionContent = reactionContentList[i],
                Count = countByReactionsList[i]
            });
        }

        return (results, total);
    }

    public async Task<List<string>> GetReactionsByIdentityAndFileAsync(OdinId identity, Guid fileId, IdentityDatabase db)
    {
        return await _db.tblDriveReactions.GetIdentityPostReactionDetailsAsync(identity, Drive.Id, fileId);
    }

    public async Task<int> GetReactionCountByIdentityAsync(OdinId odinId, Guid fileId, IdentityDatabase db)
    {
        return await _db.tblDriveReactions.GetIdentityPostReactionsAsync(odinId, Drive.Id, fileId);
    }

    public async Task<(List<Reaction>, Int32? cursor)> GetReactionsByFileAsync(int maxCount, int cursor, Guid fileId, IdentityDatabase db)
    {
        var (items, nextCursor) = await _db.tblDriveReactions.PagingByRowidAsync(db, maxCount, inCursor: cursor, driveId: Drive.Id, postIdFilter: fileId);

        var results = items.Select(item =>
            new Reaction()
            {
                FileId = new InternalDriveFileId()
                {
                    FileId = item.postId,
                    DriveId = Drive.Id
                },
                OdinId = item.identity,
                ReactionContent = item.singleReaction
            }
        ).ToList();

        return (results, nextCursor);
    }

    public async Task<(Int64 fileCount, Int64 byteSize)> GetDriveSizeInfoAsync(IdentityDatabase db)
    {
        var (count, size) = await _db.tblDriveMainIndex.GetDriveSizeDirtyAsync(Drive.Id);
        return (count, size);
    }

    public async Task<Guid?> GetByGlobalTransitIdAsync(Guid driveId, Guid globalTransitId, FileSystemType fileSystemType, IdentityDatabase db)
    {
        var record = await _db.tblDriveMainIndex.GetByGlobalTransitIdAsync(driveId, globalTransitId);
        if (null == record)
        {
            return null;
        }

        if (record.fileSystemType == (int)fileSystemType)
        {
            return record.fileId;
        }

        return null;
    }

    public async Task<Guid?> GetByClientUniqueIdAsync(Guid driveId, Guid uniqueId, FileSystemType fileSystemType, IdentityDatabase db)
    {
        var record = await _db.tblDriveMainIndex.GetByUniqueIdAsync(driveId, uniqueId);

        if (null == record)
        {
            return null;
        }

        if (record.fileSystemType == (int)fileSystemType)
        {
            return record.fileId;
        }

        return null;
    }


    private async Task<(QueryBatchCursor cursor, IEnumerable<Guid> fileIds, bool hasMoreRows)> GetBatchExplicitOrderingAsync(IOdinContext odinContext,
        FileSystemType fileSystemType, FileQueryParams qp, QueryBatchResultOptions options, IdentityDatabase db)
    {
        var securityRange = new IntRange(0, (int)odinContext.Caller.SecurityLevel);

        var aclList = GetAcl(odinContext);

        var cursor = options.Cursor;

        (var results, var hasMoreRows, cursor) = await _db.metaIndex.QueryBatchAsync(
            Drive.Id,
            noOfItems: options.MaxRecords,
            cursor,
            fileIdSort: options.Sorting == Sorting.FileId,
            newestFirstOrder: options.Ordering == Ordering.NewestFirst,
            fileSystemType: (Int32)fileSystemType,
            requiredSecurityGroup: securityRange,
            globalTransitIdAnyOf: qp.GlobalTransitId?.ToList(),
            filetypesAnyOf: qp.FileType?.ToList(),
            datatypesAnyOf: qp.DataType?.ToList(),
            fileStateAnyOf: qp.FileState?.Select(f => (int)f).ToList(),
            senderidAnyOf: qp.Sender?.ToList(),
            groupIdAnyOf: qp.GroupId?.Select(g => g).ToList(),
            userdateSpan: qp.UserDate,
            aclAnyOf: aclList?.ToList(),
            uniqueIdAnyOf: qp.ClientUniqueIdAtLeastOne?.ToList(),
            tagsAnyOf: qp.TagsMatchAtLeastOne?.ToList(),
            tagsAllOf: qp.TagsMatchAll?.ToList(),
            archivalStatusAnyOf: qp.ArchivalStatus?.ToList());

        return (cursor, results.Select(r => r), hasMoreRows);
    }
}

public class ReactionCount
{
    public string ReactionContent { get; set; }
    public int Count { get; set; }
}