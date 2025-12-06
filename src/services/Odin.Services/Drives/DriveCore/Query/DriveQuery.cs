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
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using QueryBatchCursor = Odin.Core.Storage.QueryBatchCursor;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Services.Drives.DriveCore.Query;

public class DriveQuery(
    ILogger<DriveQuery> logger,
    MainIndexMetaCached metaIndex,
    TableDriveMainIndexCached tblDriveMainIndex,
    TableDriveReactions tblDriveReactions,
    IdentityDatabase db,
    OdinIdentity odinIdentity
) : IDriveDatabaseManager
{
    public async Task<(string, List<DriveMainIndexRecord>, bool hasMoreRows)> GetModifiedCoreAsync(
        StorageDrive drive,
        IOdinContext odinContext,
        FileSystemType fileSystemType,
        FileQueryParams qp,
        QueryModifiedResultOptions options)
    {
        var callerContext = odinContext.Caller;

        var requiredSecurityGroup = new IntRange(0, (int)callerContext.SecurityLevel);
        var aclList = GetAcl(odinContext);

        // TODO TODD - use moreRows
        (var results, var moreRows, var nextCursor) = await metaIndex.QueryModifiedAsync(
            drive.Id,
            noOfItems: options.MaxRecords,
            options.Cursor,
            fileSystemType: (Int32)fileSystemType,
            stopAtModifiedUnixTimeSeconds: options.MaxDate == null
                ? null
                : new TimeRowCursor(new UnixTimeUtc(options.MaxDate.GetValueOrDefault()), null),
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

        return (nextCursor, results, moreRows);
    }


    public async Task<(QueryBatchCursor, List<DriveMainIndexRecord>, bool hasMoreRows)> GetSmartBatchCoreAsync(
        StorageDrive drive,
        IOdinContext odinContext,
        FileSystemType fileSystemType,
        FileQueryParams qp,
        QueryBatchResultOptions options)
    {
        var securityRange = new IntRange(0, (int)odinContext.Caller.SecurityLevel);
        var aclList = GetAcl(odinContext);
        var cursor = options.Cursor;

        //if (options.Ordering == QueryBatchSortOrder.Default)
        //{
        (var results, var moreRows, cursor) = await metaIndex.QueryBatchSmartCursorAsync(
            drive.Id,
            noOfItems: options.MaxRecords,
            cursor,
            sortOrder: options.Ordering,
            sortField: options.Sorting,
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
        //}

        //// if the caller was explicit in how they want results...
        //return await GetBatchExplicitOrderingAsync(drive, odinContext, fileSystemType, qp, options);
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

        (var results, var moreRows, cursor) = await metaIndex.QueryBatchAsync(
            drive.Id,
            noOfItems: options.MaxRecords,
            cursor,
            sortOrder: options.Ordering,
            sortField: options.Sorting,
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

    /// <summary>
    /// Just a ballpark figure used for quota
    /// </summary>
    /// <returns>Number of quota bytes used by this record</returns>
    public static int SizeOfDriveMainIndexRecord(DriveMainIndexRecord r)
    {
        // All the constants and overhead in DB
        // Plus the average size of reaction summary, transfer history and localAppData
        // Note that when we do a toDriveMainIndexRecord() those three fields aren't copied
        // So to make it easy, I've just added an average and ignore changes to those fields for now.
        int size = 4096;

        size += r.senderId != null ? r.senderId.Length + 20 : 0;
        size += r.hdrEncryptedKeyHeader != null ? 72 : 0;
        size += r.hdrAppData != null ? r.hdrAppData.Length + 20 : 0;
        // size += r.hdrLocalAppData != null ? r.hdrLocalAppData.Length + 20 : 0;
        // size += r.hdrReactionSummary != null ? r.hdrReactionSummary.Length + 20 : 0;
        size += r.hdrServerData != null ? r.hdrServerData.Length + 20 : 0;
        // size += r.hdrTransferHistory != null ? r.hdrTransferHistory.Length + 20 : 0;
        size += r.hdrFileMetaData != null ? r.hdrFileMetaData.Length + 20 : 0;

        return size;
    }

    public async Task SaveFileHeaderAsync(StorageDrive drive, ServerFileHeader header, Guid? useThisVersionTag = null)
    {
        var fileMetadata = header.FileMetadata;

        //sanity in case something higher up didnt set the drive properly for any crazy reason
        header.FileMetadata.File = header.FileMetadata.File with { DriveId = drive.Id };
        var driveMainIndexRecord = header.ToDriveMainIndexRecord(drive.TargetDriveInfo, odinIdentity.IdentityId);

        int headerBytes = SizeOfDriveMainIndexRecord(driveMainIndexRecord);
        var (payloadBytes, thumbBytes) = DriveStorageServiceBase.PayloadByteCount(header);
        long sumBytes = headerBytes + payloadBytes + thumbBytes;

        header.ServerMetadata.FileByteCount = sumBytes;
        driveMainIndexRecord.byteCount = sumBytes;

        var acl = new List<Guid>();
        acl.AddRange(header.ServerMetadata.AccessControlList.GetRequiredCircles());
        var ids = header.ServerMetadata.AccessControlList.GetRequiredIdentities().Select(odinId => ((OdinId)odinId).ToHashId()
        );
        acl.AddRange(ids.ToList());

        var tags = fileMetadata.AppData.Tags?.ToList();

        try
        {
            int n = await metaIndex.BaseUpsertEntryZapZapAsync(driveMainIndexRecord, acl, tags, useThisVersionTag);
            if (n != 1)
                throw new OdinClientException($"SaveFileHeaderAsync() didn't write database record, n is {n}");

            header.FileMetadata.SetCreatedModifiedWithDatabaseValue(driveMainIndexRecord.created, driveMainIndexRecord.modified);
            header.FileMetadata.VersionTag = driveMainIndexRecord.hdrVersionTag;
        }
        catch (OdinDatabaseException e) when (e.IsUniqueConstraintViolation)
        {
            DriveMainIndexRecord rf = null;
            DriveMainIndexRecord ru = null;
            DriveMainIndexRecord rt = null;

            rf = await tblDriveMainIndex.GetAsync(drive.Id, fileMetadata.File.FileId);
            if (fileMetadata.AppData.UniqueId.HasValue)
                ru = await tblDriveMainIndex.GetByUniqueIdAsync(drive.Id, fileMetadata.AppData.UniqueId.Value);
            if (fileMetadata.GlobalTransitId.HasValue)
                rt = await tblDriveMainIndex.GetByGlobalTransitIdAsync(drive.Id, fileMetadata.GlobalTransitId.Value);

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
                GuidOneOrTwo(fileMetadata.AppData.UniqueId, r?.uniqueId),
                GuidOneOrTwo(fileMetadata.GlobalTransitId, r?.globalTransitId),
                GuidOneOrTwo(drive.Id, r?.driveId),
                IntOneOrTwo((int)fileMetadata.FileState, r?.fileState ?? -1),
                IntOneOrTwo((int)header.ServerMetadata.FileSystemType, r?.fileSystemType ?? -1),
                GuidOneOrTwo(fileMetadata.File.FileId, r.fileId),
                drive.Name);

            throw new OdinClientException($"UniqueId [{fileMetadata.AppData.UniqueId}] not unique.",
                OdinClientErrorCode.ExistingFileWithUniqueId);
        }
    }

    public async Task SaveLocalMetadataAsync(Guid driveId, Guid fileId, Guid oldVersionTag, string metadataJson, Guid newVersionTag)
    {
        var exists = await db.DriveMainIndexCached.UpdateLocalAppMetadataAsync(driveId, fileId, oldVersionTag, newVersionTag, metadataJson);

        if (exists == false)
            throw new OdinClientException("No such file found for local metadata async", OdinClientErrorCode.FileNotFound);
    }

    public async Task SaveLocalMetadataTagsAsync(Guid driveId, Guid fileId, LocalAppMetadata metadata, Guid newVersionTag)
    {
        await using var tx = await db.BeginStackedTransactionAsync();

        // Update the official metadata field
        var json = OdinSystemSerializer.Serialize(metadata);
        var exists = await db.DriveMainIndexCached.UpdateLocalAppMetadataAsync(driveId, fileId, metadata.VersionTag, newVersionTag, json);

        if (exists == false)
            throw new OdinClientException("No such file found for local metadata async", OdinClientErrorCode.FileNotFound);

        // Update the tables used to query
        await db.MainIndexMetaCached.UpdateLocalTagsAsync(driveId, fileId, metadata.Tags);

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
        int n = await metaIndex.DeleteEntryAsync(drive.Id, file.FileId);
        if (n < 1)
            throw new OdinSystemException("HardDeleteFileHeaderAsync() unable to delete header");
    }

    public async Task AddReactionAsync(StorageDrive drive, OdinId odinId, Guid fileId, string reaction,
        WriteSecondDatabaseRowBase markComplete)
    {
        bool inserted = false;

        await using (var tx = await db.BeginStackedTransactionAsync())
        {
            // inserted will be false if the reaction was already in the database
            inserted = await tblDriveReactions.TryInsertAsync(new DriveReactionsRecord()
            {
                driveId = drive.Id,
                identity = odinId,
                postId = fileId,
                singleReaction = reaction
            });

            logger.LogDebug("{method} -> markComplete {message}", 
                nameof(AddReactionAsync),
                markComplete == null ? "is not configured" : "will be called");
            
            if (markComplete != null)
            {
                int n = await markComplete.ExecuteAsync();

                if (n != 1)
                    throw new OdinSystemException("Hum, unable to mark the inbox record as completed, aborting");
            }

            tx.Commit();
        }

        // Both these exception need to be scrutinized. Look up in the inbox handler. We need to 
        // be very deliberate about when we remove it from the inbox and when we try again. If for
        // example we fail to markComplete, but the reaction insert was successful, we should try again,
        // but otherwise we should delete from inbox !!
        if (!inserted)
            throw new OdinClientException("Cannot add duplicate reaction");
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
        var (items, nextCursor) = await tblDriveReactions.PagingByRowidAsync(maxCount, inCursor: cursor, driveId: drive.Id,
            postIdFilter: fileId);

        var results = items.Select(item => new Reaction()
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
        var (count, size) = await tblDriveMainIndex.GetDriveSizeAsync(drive.Id);
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

/*
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
            sortField: options.Sorting,
            sortOrder: options.Ordering,
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
*/
}

public class ReactionCount
{
    public string ReactionContent { get; set; }
    public int Count { get; set; }
}