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

    public Task UpdateCurrentIndex(ServerFileHeader header, DatabaseConnection cn)
    {
        if (null == header)
        {
            logger.LogWarning("UpdateCurrentIndex called on null server file header");
            return Task.CompletedTask;
        }

        var metadata = header.FileMetadata;

        int securityGroup = (int)header.ServerMetadata.AccessControlList.RequiredSecurityGroup;
        var exists = _db.tblDriveMainIndex.Get(cn, Drive.Id, metadata.File.FileId) != null;

        if (header.ServerMetadata.DoNotIndex)
        {
            if (exists) // clean up if the flag was changed after it was indexed
            {
                return RemoveFromCurrentIndex(metadata.File, cn);
            }
        }

        var sender = string.IsNullOrEmpty(metadata.SenderOdinId)
            ? Array.Empty<byte>()
            : ((OdinId)metadata.SenderOdinId).ToByteArray();
        
        var acl = new List<Guid>();
        acl.AddRange(header.ServerMetadata.AccessControlList.GetRequiredCircles());
        var ids = header.ServerMetadata.AccessControlList.GetRequiredIdentities().Select(odinId =>
            ((OdinId)odinId).ToHashId()
        );
        acl.AddRange(ids.ToList());

        var tags = metadata.AppData.Tags?.ToList();

        if (exists)
        {
            _db.UpdateEntryZapZap(
                cn,
                Drive.Id,
                fileId: metadata.File.FileId,
                fileType: metadata.AppData.FileType,
                dataType: metadata.AppData.DataType,
                senderId: sender,
                groupId: metadata.AppData.GroupId,
                uniqueId: metadata.AppData.UniqueId,
                archivalStatus: metadata.AppData.ArchivalStatus,
                userDate: metadata.AppData.UserDate,
                requiredSecurityGroup: securityGroup,
                accessControlList: acl,
                tagIdList: tags,
                fileState: (int)metadata.FileState,
                byteCount: header.ServerMetadata.FileByteCount,
                fileSystemType: (int)header.ServerMetadata.FileSystemType);
        }
        else
        {
            try
            {
                _db.AddEntry(
                    cn,
                    Drive.Id,
                    fileId: metadata.File.FileId,
                    globalTransitId: metadata.GlobalTransitId,
                    fileType: metadata.AppData.FileType,
                    dataType: metadata.AppData.DataType,
                    senderId: sender,
                    groupId: metadata.AppData.GroupId,
                    uniqueId: metadata.AppData.UniqueId,
                    archivalStatus: metadata.AppData.ArchivalStatus,
                    userDate: metadata.AppData.UserDate.GetValueOrDefault(),
                    requiredSecurityGroup: securityGroup,
                    accessControlList: acl,
                    tagIdList: tags,
                    fileState: (int)metadata.FileState,
                    fileSystemType: (int)header.ServerMetadata.FileSystemType,
                    byteCount: header.ServerMetadata.FileByteCount
                );
            }
            catch (SqliteException e)
            {
                if (e.SqliteErrorCode == 19 || e.ErrorCode == 19 || e.SqliteExtendedErrorCode == 19)
                {
                    // logger.LogError("SqliteErrorCode:19 - UniqueId:{uid}.  GlobalTransitId:{gtid}.  DriveId:{driveId}", metadata.AppData.UniqueId, metadata.GlobalTransitId, Drive.Id);
                    throw new OdinClientException($"UniqueId [{metadata.AppData.UniqueId}] not unique.", OdinClientErrorCode.ExistingFileWithUniqueId);
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Todd says it aint soft and it aint hard - mushy it is
    /// </summary>
    /// <param name="header"></param>
    /// <param name="cn"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="OdinClientException"></exception>
    public Task MushyDelete(ServerFileHeader header, DatabaseConnection cn)
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

        var sender = string.IsNullOrEmpty(metadata.SenderOdinId)  // <--- REVIEW null means it should update, 00000 will overwrite
            ? Array.Empty<byte>()
            : ((OdinId)metadata.SenderOdinId).ToByteArray();

        var acl = new List<Guid>();
        acl.AddRange(header.ServerMetadata.AccessControlList.GetRequiredCircles());
        var ids = header.ServerMetadata.AccessControlList.GetRequiredIdentities().Select(odinId =>
            ((OdinId)odinId).ToHashId()
        );
        acl.AddRange(ids.ToList());

        var tags = metadata.AppData.Tags?.ToList();

        //
        // What is really the purpose of this update @todd?
        // Is this function updating some fields? Or is it MushyDeleting an item?
        // What does it mean to mushy-delete? Which fields are supposed to be zapped?
        // Shouldn't those fields be set to "null" below rather than arbitrary values from the argument...
        //
        int n = _db.UpdateEntryZapZap(
            cn,
            Drive.Id,
            fileId: metadata.File.FileId,
            fileType: metadata.AppData.FileType,
            dataType: metadata.AppData.DataType,
            senderId: sender,
            groupId: metadata.AppData.GroupId,
            uniqueId: metadata.AppData.UniqueId,
            archivalStatus: metadata.AppData.ArchivalStatus,
            userDate: metadata.AppData.UserDate,
            requiredSecurityGroup: securityGroup,
            accessControlList: acl,
            tagIdList: tags,
            fileState: (int)metadata.FileState,
            byteCount: header.ServerMetadata.FileByteCount,
            fileSystemType: (int)header.ServerMetadata.FileSystemType);

        // _db.tblDriveMainIndex.SoftDelete(cn, Drive.Id, metadata.File.FileId);


        if (n < 1)
            throw new OdinSystemException($"file to MushyDelete does not exist driveId {Drive.Id} fileId {metadata.File.FileId}");

        return Task.CompletedTask;
    }



    public Task RemoveFromCurrentIndex(InternalDriveFileId file, DatabaseConnection cn)
    {
        _db.DeleteEntry(cn, Drive.Id, file.FileId);
        return Task.CompletedTask;
    }

    public Task LoadLatestIndex(DatabaseConnection cn)
    {
        _db.CreateDatabase(cn, false);
        return Task.CompletedTask;
    }

    public Task AddCommandMessage(List<Guid> fileIds, DatabaseConnection cn)
    {
        _db.tblDriveCommandMessageQueue.InsertRows(cn, Drive.Id, fileIds);
        return Task.CompletedTask;
    }

    public Task<List<UnprocessedCommandMessage>> GetUnprocessedCommands(int count, DatabaseConnection cn)
    {
        var list = _db.tblDriveCommandMessageQueue.Get(cn, Drive.Id, count) ?? new List<DriveCommandMessageQueueRecord>();

        var result = list.Select(x => new UnprocessedCommandMessage()
        {
            Id = x.fileId,
            Received = x.timeStamp
        }).ToList();

        return Task.FromResult(result);
    }

    public Task MarkCommandsCompleted(List<Guid> fileIds, DatabaseConnection cn)
    {
        _db.tblDriveCommandMessageQueue.DeleteRow(cn, Drive.Id, fileIds);
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
        var (count, size) = _db.tblDriveMainIndex.GetDriveSizeDirty(cn, Drive.Id);
        return Task.FromResult((count, size));
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