using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using QueryBatchCursor = Odin.Core.Storage.SQLite.IdentityDatabase.QueryBatchCursor;

namespace Odin.Services.Drives.DriveCore.Query.Sqlite;

public class SqliteDatabaseManager(TenantSystemStorage tenantSystemStorage, StorageDrive drive, ILogger<object> logger)
    : IDriveDatabaseManager
{
    private readonly IdentityDatabase _db = tenantSystemStorage.IdentityDatabase;

    public StorageDrive Drive { get; init; } = drive;

    public Task<(long, IEnumerable<Guid>, bool hasMoreRows)> GetModifiedCore(IOdinContext odinContext, FileSystemType fileSystemType,
        FileQueryParams qp, QueryModifiedResultOptions options, DatabaseConnection cn)
    {
        var callerContext = odinContext.Caller;

        var requiredSecurityGroup = new IntRange(0, (int)callerContext.SecurityLevel);
        var aclList = GetAcl(odinContext);
        var cursor = new UnixTimeUtcUnique(options.Cursor);

        // TODO TODD - use moreRows
        var (results, moreRows) = _db.QueryModified(
            cn,
            Drive.Id,
            noOfItems: options.MaxRecords,
            cursor: ref cursor,
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

        return Task.FromResult((cursor.uniqueTime, results.AsEnumerable(), moreRows));
    }


    public Task<(QueryBatchCursor, IEnumerable<Guid>, bool hasMoreRows)> GetBatchCore(IOdinContext odinContext,
        FileSystemType fileSystemType, FileQueryParams qp, QueryBatchResultOptions options, DatabaseConnection cn)
    {
        var securityRange = new IntRange(0, (int)odinContext.Caller.SecurityLevel);
        var aclList = GetAcl(odinContext);
        var cursor = options.Cursor;

        if (options.Ordering == Ordering.Default)
        {
            var (results, moreRows) = _db.QueryBatchAuto(
                cn,
                Drive.Id,
                noOfItems: options.MaxRecords,
                cursor: ref cursor,
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

            return Task.FromResult((cursor, results.Select(r => r), moreRows));
        }

        // if the caller was explicit in how they want results...
        return GetBatchExplicitOrdering(odinContext, fileSystemType, qp, options, cn);
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

    public Task UpdateCurrentIndex(ServerFileHeader header, DatabaseConnection cn)
    {
        if (null == header)
        {
            logger.LogWarning("UpdateCurrentIndex called on null server file header");
            return Task.CompletedTask;
        }

        var metadata = header.FileMetadata;

        int securityGroup = (int)header.ServerMetadata.AccessControlList.RequiredSecurityGroup;

        // This really doesn't belong here IMO, delete should be handled before this is called and never called with this flag
        if (header.ServerMetadata.DoNotIndex)
        {
            return HardDeleteFromIndex(metadata.File, cn);
        }

        var acl = new List<Guid>();
        acl.AddRange(header.ServerMetadata.AccessControlList.GetRequiredCircles());
        var ids = header.ServerMetadata.AccessControlList.GetRequiredIdentities().Select(odinId =>
            ((OdinId)odinId).ToHashId()
        );
        acl.AddRange(ids.ToList());

        var tags = metadata.AppData.Tags?.ToList();

        var driveMainIndexRecord = new DriveMainIndexRecord()
        {
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
            byteCount = header.ServerMetadata.FileByteCount
        };

        try
        {
            _db.BaseUpsertEntryZapZap(driveMainIndexRecord, acl, tags);
            // driveMainIndexRecord created / modified contain the values written to the database
            // @todd you might consider doing this:
            // using (CreateCommitUnitOfWork()) {
            //   r = UpdateCurrentIndex(...);
            //   header.created = r.created;
            //   header.modified = r.modified;
            //   WriteFileToDisk(... header ...);
            // }
            // Thus you prepare to commit the data to the index, and unless the file write throws an error it commits the transaction
        }
        catch (SqliteException e)
        {
            if (e.SqliteErrorCode == 19)
            {
                DriveMainIndexRecord rf = null;
                DriveMainIndexRecord ru = null;
                DriveMainIndexRecord rt = null;

                rf = _db.tblDriveMainIndex.Get(Drive.Id, metadata.File.FileId);
                if (metadata.AppData.UniqueId.HasValue)
                    ru = _db.tblDriveMainIndex.GetByUniqueId(Drive.Id, metadata.AppData.UniqueId);
                if (metadata.GlobalTransitId.HasValue)
                    rt = _db.tblDriveMainIndex.GetByGlobalTransitId(Drive.Id, metadata.GlobalTransitId);

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

                logger.LogError(
                    "SqliteErrorCode:19 (found: [{index}]) - UniqueId:{uid}.  GlobalTransitId:{gtid}.  DriveId:{driveId}.   FileState {fileState}.   FileSystemType {fileSystemType}.  FileId {fileId}",
                    s,
                    GuidOneOrTwo(metadata.AppData.UniqueId, r?.uniqueId),
                    GuidOneOrTwo(metadata.GlobalTransitId, r?.globalTransitId),
                    GuidOneOrTwo(Drive.Id, r?.driveId),
                    IntOneOrTwo((int)metadata.FileState, r?.fileState ?? -1),
                    IntOneOrTwo((int)header.ServerMetadata.FileSystemType, r?.fileSystemType ?? -1),
                    GuidOneOrTwo(metadata.File.FileId, r.fileId));

                throw new OdinClientException($"UniqueId [{metadata.AppData.UniqueId}] not unique.", OdinClientErrorCode.ExistingFileWithUniqueId);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Soft deleting a file
    /// </summary>
    /// <param name="header"></param>
    /// <param name="cn"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="OdinClientException"></exception>
    public Task SoftDeleteFromIndex(ServerFileHeader header, DatabaseConnection cn)
    {
        if (null == header)
        {
            logger.LogWarning("SoftDelete called on null server file header");
            return Task.CompletedTask;
        }

        if (header.ServerMetadata.DoNotIndex)
            throw new ArgumentException("SoftDelete called with DoNotIndex (hard-delete)");

        var metadata = header.FileMetadata;
        int securityGroup = (int)header.ServerMetadata.AccessControlList.RequiredSecurityGroup;

        var acl = new List<Guid>();
        acl.AddRange(header.ServerMetadata.AccessControlList.GetRequiredCircles());
        var ids = header.ServerMetadata.AccessControlList.GetRequiredIdentities().Select(odinId =>
            ((OdinId)odinId).ToHashId()
        );
        acl.AddRange(ids.ToList());

        var tags = metadata.AppData.Tags?.ToList();

        //Note: we set the fields to exactly what is stored in the file from the DriveStorageBase class
        var driveMainIndexRecord = new DriveMainIndexRecord()
        {
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
            byteCount = header.ServerMetadata.FileByteCount
        };

        int n = _db.BaseUpdateEntryZapZap(cn, driveMainIndexRecord, acl, tags);

        // @todd The modified timestamp in driveMainIndexRecord will be updated with the value written to the index

        if (n < 1)
            throw new OdinSystemException($"file to SoftDelete does not exist driveId {Drive.Id} fileId {metadata.File.FileId}");

        return Task.CompletedTask;
    }

    public Task HardDeleteFromIndex(InternalDriveFileId file, DatabaseConnection cn)
    {
        _db.DeleteEntry(cn, Drive.Id, file.FileId);
        return Task.CompletedTask;
    }

    public Task LoadLatestIndex(DatabaseConnection cn)
    {
        _db.CreateDatabase(false);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        // NO! Database is not owned by this class
        // _db.Commit();
        // _db.Dispose();
    }

    public void AddReaction(OdinId odinId, Guid fileId, string reaction, DatabaseConnection cn)
    {
        _db.tblDriveReactions.Insert(cn, new DriveReactionsRecord()
        {
            driveId = Drive.Id,
            identity = odinId,
            postId = fileId,
            singleReaction = reaction
        });
    }

    public void DeleteReactions(OdinId odinId, Guid fileId, DatabaseConnection cn)
    {
        _db.tblDriveReactions.DeleteAllReactions(cn, Drive.Id, odinId, fileId);
    }

    public void DeleteReaction(OdinId odinId, Guid fileId, string reaction, DatabaseConnection cn)
    {
        _db.tblDriveReactions.Delete(cn, Drive.Id, odinId, fileId, reaction);
    }

    public (List<string>, int) GetReactions(Guid fileId, DatabaseConnection cn)
    {
        return _db.tblDriveReactions.GetPostReactions(cn, Drive.Id, fileId);
    }

    public (List<ReactionCount> reactions, int total) GetReactionSummaryByFile(Guid fileId, DatabaseConnection cn)
    {
        var (reactionContentList, countByReactionsList, total) = _db.tblDriveReactions.GetPostReactionsWithDetails(cn, Drive.Id, fileId);

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

    public List<string> GetReactionsByIdentityAndFile(OdinId identity, Guid fileId, DatabaseConnection cn)
    {
        return _db.tblDriveReactions.GetIdentityPostReactionDetails(cn, identity, Drive.Id, fileId);
    }

    public int GetReactionCountByIdentity(OdinId odinId, Guid fileId, DatabaseConnection cn)
    {
        return _db.tblDriveReactions.GetIdentityPostReactions(cn, odinId, Drive.Id, fileId);
    }

    public (List<Reaction>, Int32? cursor) GetReactionsByFile(int maxCount, int cursor, Guid fileId, DatabaseConnection cn)
    {
        var items = _db.tblDriveReactions.PagingByRowid(cn, maxCount, inCursor: cursor, out var nextCursor, driveId: Drive.Id, postIdFilter: fileId);

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

    public Task<(Int64 fileCount, Int64 byteSize)> GetDriveSizeInfo(DatabaseConnection cn)
    {
        var (count, size) = _db.tblDriveMainIndex.GetDriveSizeDirty(Drive.Id);
        return Task.FromResult((count, size));
    }

    public Task<Guid?> GetByGlobalTransitId(Guid driveId, Guid globalTransitId, FileSystemType fileSystemType, DatabaseConnection cn)
    {
        var record = _db.tblDriveMainIndex.GetByGlobalTransitId(driveId, globalTransitId);
        if (null == record)
        {
            return Task.FromResult((Guid?)null);
        }

        if (record.fileSystemType == (int)fileSystemType)
        {
            return Task.FromResult((Guid?)record.fileId);
        }

        return Task.FromResult((Guid?)null);
    }
    
    public Task<Guid?> GetByClientUniqueId(Guid driveId, Guid uniqueId, FileSystemType fileSystemType, DatabaseConnection cn)
    {
        var record = _db.tblDriveMainIndex.GetByUniqueId(driveId, uniqueId);
        
        if (null == record)
        {
            return Task.FromResult((Guid?)null);
        }

        if (record.fileSystemType == (int)fileSystemType)
        {
            return Task.FromResult((Guid?)record.fileId);
        }

        return Task.FromResult((Guid?)null);
    }

    private Task<(QueryBatchCursor cursor, IEnumerable<Guid> fileIds, bool hasMoreRows)> GetBatchExplicitOrdering(IOdinContext odinContext,
        FileSystemType fileSystemType, FileQueryParams qp, QueryBatchResultOptions options, DatabaseConnection cn)
    {
        var securityRange = new IntRange(0, (int)odinContext.Caller.SecurityLevel);

        var aclList = GetAcl(odinContext);

        var cursor = options.Cursor;

        var (results, hasMoreRows) = _db.QueryBatch(
            cn,
            Drive.Id,
            noOfItems: options.MaxRecords,
            cursor: ref cursor,
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

        return Task.FromResult((cursor, results.Select(r => r), hasMoreRows));
    }
}

public class ReactionCount
{
    public string ReactionContent { get; set; }
    public int Count { get; set; }
}