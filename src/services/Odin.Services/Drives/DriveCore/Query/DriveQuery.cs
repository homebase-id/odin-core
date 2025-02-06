using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using QueryBatchCursor = Odin.Core.Storage.QueryBatchCursor;

namespace Odin.Services.Drives.DriveCore.Query;

public class DriveQuery(
    ILogger<DriveQuery> logger,
    MainIndexMeta metaIndex,
    TableDriveMainIndex tblDriveMainIndex,
    TableDriveReactions tblDriveReactions,
    IdentityDatabase db
) : IDriveDatabaseManager
{
    public async Task<(long, List<DriveMainIndexRecord>, bool hasMoreRows)> GetModifiedCoreAsync(
        StorageDrive drive,
        IOdinContext odinContext,
        FileSystemType fileSystemType,
        FileQueryParams qp,
        QueryModifiedResultOptions options)
    {
        var callerContext = odinContext.Caller;

        var requiredSecurityGroup = new IntRange(0, (int)callerContext.SecurityLevel);
        var aclList = GetAcl(odinContext);
        Int64.TryParse(options.Cursor, out long c);

        var cursor = new UnixTimeUtcUnique(c);

        // TODO TODD - use moreRows
        (var results, var moreRows, cursor) = await metaIndex.QueryModifiedAsync(
            drive.Id,
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
            archivalStatusAnyOf: qp.ArchivalStatus?.ToList(),
            localTagsAllOf: qp.LocalTagsMatchAll?.ToList(),
            localTagsAnyOf: qp.LocalTagsMatchAtLeastOne?.ToList());

        return (cursor.uniqueTime, results, moreRows);
    }


    public async Task<(QueryBatchCursor, List<DriveMainIndexRecord>, bool hasMoreRows)> GetBatchCoreAsync(
        StorageDrive drive,
        IOdinContext odinContext,
        FileSystemType fileSystemType,
        FileQueryParams qp,
        QueryBatchResultOptions options)
    {
        var securityRange = new IntRange(0, (int)odinContext.Caller.SecurityLevel);
        var aclList = GetAcl(odinContext);
        var cursor = options.Cursor;

        if (options.Ordering == Ordering.Default)
        {
            (var results, var moreRows, cursor) = await metaIndex.QueryBatchAutoAsync(
                drive.Id,
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
                archivalStatusAnyOf: qp.ArchivalStatus?.ToList(),
                localTagsAllOf: qp.LocalTagsMatchAll?.ToList(),
                localTagsAnyOf: qp.LocalTagsMatchAtLeastOne?.ToList());

            return (cursor, results, moreRows);
        }

        // if the caller was explicit in how they want results...
        return await GetBatchExplicitOrderingAsync(drive, odinContext, fileSystemType, qp, options);
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

    public async Task SaveFileHeaderAsync(StorageDrive drive, ServerFileHeader header)
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
            driveId = drive.Id,
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

            // local data is updated by a specific method
            // hdrLocalVersionTag =  ...
            // hdrLocalAppData = ...

            //this is updated by the SaveReactionSummary method
            // hdrReactionSummary = OdinSystemSerializer.Serialize(header.FileMetadata.ReactionPreview),
            // this is handled by the SaveTransferHistory method
            // hdrTransferStatus = OdinSystemSerializer.Serialize(header.ServerMetadata.TransferHistory),

            hdrTmpDriveAlias = drive.TargetDriveInfo.Alias,
            hdrTmpDriveType = drive.TargetDriveInfo.Type,
        };

        if (driveMainIndexRecord.driveId == Guid.Empty || driveMainIndexRecord.fileId == Guid.Empty)
        {
            throw new OdinSystemException("DriveId and FileId must be a non-empty GUID");
        }

        try
        {
            await metaIndex.BaseUpsertEntryZapZapAsync(driveMainIndexRecord, acl, tags);
        }
        catch (OdinDatabaseException e) when (e.IsUniqueConstraintViolation)
        {
            DriveMainIndexRecord rf = null;
            DriveMainIndexRecord ru = null;
            DriveMainIndexRecord rt = null;

            rf = await tblDriveMainIndex.GetAsync(drive.Id, metadata.File.FileId);
            if (metadata.AppData.UniqueId.HasValue)
                ru = await tblDriveMainIndex.GetByUniqueIdAsync(drive.Id, metadata.AppData.UniqueId);
            if (metadata.GlobalTransitId.HasValue)
                rt = await tblDriveMainIndex.GetByGlobalTransitIdAsync(drive.Id, metadata.GlobalTransitId);

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
                "IsUniqueConstraintViolation (found: [{index}]) - UniqueId:{uid}.  GlobalTransitId:{gtid}.  DriveId:{driveId}.   FileState {fileState}.   FileSystemType {fileSystemType}.  FileId {fileId}.  DriveName {driveName}",
                s,
                GuidOneOrTwo(metadata.AppData.UniqueId, r?.uniqueId),
                GuidOneOrTwo(metadata.GlobalTransitId, r?.globalTransitId),
                GuidOneOrTwo(drive.Id, r?.driveId),
                IntOneOrTwo((int)metadata.FileState, r?.fileState ?? -1),
                IntOneOrTwo((int)header.ServerMetadata.FileSystemType, r?.fileSystemType ?? -1),
                GuidOneOrTwo(metadata.File.FileId, r.fileId),
                drive.Name);

            throw new OdinClientException($"UniqueId [{metadata.AppData.UniqueId}] not unique.",
                OdinClientErrorCode.ExistingFileWithUniqueId);
        }
    }

    public async Task SaveLocalMetadataAsync(Guid driveId, Guid fileId, Guid newVersionTag, string metadataJson)
    {
        await db.DriveLocalTagIndex.UpdateLocalAppMetadataAsync(driveId, fileId, newVersionTag, metadataJson);
    }

    public async Task SaveLocalMetadataTagsAsync(Guid driveId, Guid fileId, LocalAppMetadata metadata)
    {
        await using var tx = await db.BeginStackedTransactionAsync();

        // Update the tables used to query
        await db.DriveLocalTagIndex.UpdateLocalTagsAsync(driveId, fileId, metadata.Tags);

        // Update the official metadata field
        var json = OdinSystemSerializer.Serialize(metadata);
        await db.DriveLocalTagIndex.UpdateLocalAppMetadataAsync(driveId, fileId, metadata.VersionTag, json);

        tx.Commit();
    }
    
    public async Task SaveReactionSummary(StorageDrive drive, Guid fileId, ReactionSummary summary)
    {
        var json = summary == null ? "" : OdinSystemSerializer.Serialize(summary);
        await tblDriveMainIndex.UpdateReactionSummaryAsync(drive.Id, fileId, json);
    }

    public async Task<ServerFileHeader> GetFileHeaderAsync(StorageDrive drive, Guid fileId, FileSystemType fileSystemType)
    {
        var record = await tblDriveMainIndex.GetAsync(drive.Id, fileId);

        if (null == record || record.fileSystemType != (int)fileSystemType)
        {
            return null;
        }

        return ServerFileHeader.FromDriveMainIndexRecord(record);
    }

    /// <summary>
    /// Soft deleting a file
    /// </summary>
    /// <param name="header"></param>
    /// <param name="db"></param>
    /// <returns></returns>
    public Task SoftDeleteFileHeader(ServerFileHeader header)
    {
        throw new NotImplementedException("No longer needed, this will be removed");
    }

    public async Task HardDeleteFileHeaderAsync(StorageDrive drive, InternalDriveFileId file)
    {
        await metaIndex.DeleteEntryAsync(drive.Id, file.FileId);
    }

    public async Task AddReactionAsync(StorageDrive drive, OdinId odinId, Guid fileId, string reaction)
    {
        var reactionAdded = await tblDriveReactions.TryInsertAsync(new DriveReactionsRecord()
        {
            driveId = drive.Id,
            identity = odinId,
            postId = fileId,
            singleReaction = reaction
        });

        if (!reactionAdded)
        {
            throw new OdinClientException("Cannot add duplicate reaction");
        }
    }

    public async Task DeleteReactionsAsync(StorageDrive drive, OdinId odinId, Guid fileId)
    {
        await tblDriveReactions.DeleteAllReactionsAsync(drive.Id, odinId, fileId);
    }

    public async Task DeleteReactionAsync(StorageDrive drive, OdinId odinId, Guid fileId, string reaction)
    {
        await tblDriveReactions.DeleteAsync(drive.Id, odinId, fileId, reaction);
    }

    public async Task<(List<string>, int)> GetReactionsAsync(StorageDrive drive, Guid fileId)
    {
        return await tblDriveReactions.GetPostReactionsAsync(drive.Id, fileId);
    }

    public async Task<(List<ReactionCount> reactions, int total)> GetReactionSummaryByFileAsync(StorageDrive drive, Guid fileId)
    {
        var (reactionContentList, countByReactionsList, total) = await tblDriveReactions.GetPostReactionsWithDetailsAsync(drive.Id, fileId);

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

    public async Task<List<string>> GetReactionsByIdentityAndFileAsync(StorageDrive drive, OdinId identity, Guid fileId)
    {
        return await tblDriveReactions.GetIdentityPostReactionDetailsAsync(identity, drive.Id, fileId);
    }

    public async Task<int> GetReactionCountByIdentityAsync(StorageDrive drive, OdinId odinId, Guid fileId)
    {
        return await tblDriveReactions.GetIdentityPostReactionsAsync(odinId, drive.Id, fileId);
    }

    public async Task<(List<Reaction>, Int32? cursor)> GetReactionsByFileAsync(StorageDrive drive, int maxCount, int cursor, Guid fileId)
    {
        var (items, nextCursor) =
            await tblDriveReactions.PagingByRowidAsync(maxCount, inCursor: cursor, driveId: drive.Id, postIdFilter: fileId);

        var results = items.Select(item =>
            new Reaction()
            {
                FileId = new InternalDriveFileId()
                {
                    FileId = item.postId,
                    DriveId = drive.Id
                },
                OdinId = item.identity,
                ReactionContent = item.singleReaction
            }
        ).ToList();

        return (results, nextCursor);
    }

    public async Task<(Int64 fileCount, Int64 byteSize)> GetDriveSizeInfoAsync(StorageDrive drive)
    {
        var (count, size) = await tblDriveMainIndex.GetDriveSizeDirtyAsync(drive.Id);
        return (count, size);
    }

    public async Task<DriveMainIndexRecord> GetByGlobalTransitIdAsync(Guid driveId, Guid globalTransitId, FileSystemType fileSystemType)
    {
        var record = await tblDriveMainIndex.GetByGlobalTransitIdAsync(driveId, globalTransitId);
        if (null == record)
        {
            return null;
        }

        if (record.fileSystemType == (int)fileSystemType)
        {
            return record;
        }

        return null;
    }

    public async Task<DriveMainIndexRecord> GetByClientUniqueIdAsync(Guid driveId, Guid uniqueId, FileSystemType fileSystemType)
    {
        var record = await tblDriveMainIndex.GetByUniqueIdAsync(driveId, uniqueId);

        if (null == record)
        {
            return null;
        }

        if (record.fileSystemType == (int)fileSystemType)
        {
            return record;
        }

        return null;
    }

  
    private async Task<(QueryBatchCursor cursor, List<DriveMainIndexRecord> fileIds, bool hasMoreRows)> GetBatchExplicitOrderingAsync(
        StorageDrive drive,
        IOdinContext odinContext,
        FileSystemType fileSystemType, FileQueryParams qp, QueryBatchResultOptions options)
    {
        var securityRange = new IntRange(0, (int)odinContext.Caller.SecurityLevel);

        var aclList = GetAcl(odinContext);

        var cursor = options.Cursor;

        (var results, var hasMoreRows, cursor) = await metaIndex.QueryBatchAsync(
            drive.Id,
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

        return (cursor, results, hasMoreRows);
    }
}

public class ReactionCount
{
    public string ReactionContent { get; set; }
    public int Count { get; set; }
}