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
        FileQueryParams qp, QueryModifiedResultOptions options)
    {
        var callerContext = odinContext.Caller;

        var requiredSecurityGroup = new IntRange(0, (int)callerContext.SecurityLevel);
        var aclList = GetAcl(odinContext);
        var cursor = new UnixTimeUtcUnique(options.Cursor);

        using var cn = tenantSystemStorage.CreateConnection();

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
        FileSystemType fileSystemType, FileQueryParams qp, QueryBatchResultOptions options)
    {
        var securityRange = new IntRange(0, (int)odinContext.Caller.SecurityLevel);
        var aclList = GetAcl(odinContext);
        var cursor = options.Cursor;

        using var cn = tenantSystemStorage.CreateConnection();

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
        return GetBatchExplicitOrdering(odinContext, fileSystemType, qp, options);
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

    public Task UpdateCurrentIndex(ServerFileHeader header)
    {
        if (null == header)
        {
            logger.LogWarning("UpdateCurrentIndex called on null server file header");
            return Task.CompletedTask;
        }

        var metadata = header.FileMetadata;

        int securityGroup = (int)header.ServerMetadata.AccessControlList.RequiredSecurityGroup;
        using var cn = tenantSystemStorage.CreateConnection();
        var exists = _db.tblDriveMainIndex.Get(cn, Drive.Id, metadata.File.FileId) != null;

        if (header.ServerMetadata.DoNotIndex)
        {
            if (exists) // clean up if the flag was changed after it was indexed
            {
                _db.tblDriveMainIndex.Delete(cn, Drive.Id, metadata.File.FileId);
            }

            return Task.CompletedTask;
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
                    throw new OdinClientException($"UniqueId [{metadata.AppData.UniqueId}] not unique.", OdinClientErrorCode.ExistingFileWithUniqueId);
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task RemoveFromCurrentIndex(InternalDriveFileId file)
    {
        using var cn = tenantSystemStorage.CreateConnection();
        _db.DeleteEntry(cn, Drive.Id, file.FileId);
        return Task.CompletedTask;
    }

    public Task LoadLatestIndex()
    {
        using var cn = tenantSystemStorage.CreateConnection();
        _db.CreateDatabase(cn, false);
        return Task.CompletedTask;
    }

    public Task AddCommandMessage(List<Guid> fileIds)
    {
        using var cn = tenantSystemStorage.CreateConnection();
        _db.tblDriveCommandMessageQueue.InsertRows(cn, Drive.Id, fileIds);
        return Task.CompletedTask;
    }

    public Task<List<UnprocessedCommandMessage>> GetUnprocessedCommands(int count)
    {
        using var cn = tenantSystemStorage.CreateConnection();
        var list = _db.tblDriveCommandMessageQueue.Get(cn, Drive.Id, count) ?? new List<DriveCommandMessageQueueRecord>();

        var result = list.Select(x => new UnprocessedCommandMessage()
        {
            Id = x.fileId,
            Received = x.timeStamp
        }).ToList();

        return Task.FromResult(result);
    }

    public Task MarkCommandsCompleted(List<Guid> fileIds)
    {
        using var cn = tenantSystemStorage.CreateConnection();
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

    public void AddReaction(OdinId odinId, Guid fileId, string reaction)
    {
        using var cn = tenantSystemStorage.CreateConnection();
        _db.tblDriveReactions.Insert(cn, new DriveReactionsRecord()
        {
            driveId = Drive.Id,
            identity = odinId,
            postId = fileId,
            singleReaction = reaction
        });
    }

    public void DeleteReactions(OdinId odinId, Guid fileId)
    {
        using var cn = tenantSystemStorage.CreateConnection();
        _db.tblDriveReactions.DeleteAllReactions(cn, Drive.Id, odinId, fileId);
    }

    public void DeleteReaction(OdinId odinId, Guid fileId, string reaction)
    {
        using var cn = tenantSystemStorage.CreateConnection();
        _db.tblDriveReactions.Delete(cn, Drive.Id, odinId, fileId, reaction);
    }

    public (List<string>, int) GetReactions(Guid fileId)
    {
        using var cn = tenantSystemStorage.CreateConnection();
        return _db.tblDriveReactions.GetPostReactions(cn, Drive.Id, fileId);
    }

    public (List<ReactionCount> reactions, int total) GetReactionSummaryByFile(Guid fileId)
    {
        using var cn = tenantSystemStorage.CreateConnection();
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

    public List<string> GetReactionsByIdentityAndFile(OdinId identity, Guid fileId)
    {
        using var cn = tenantSystemStorage.CreateConnection();
        return _db.tblDriveReactions.GetIdentityPostReactionDetails(cn, identity, Drive.Id, fileId);
    }

    public int GetReactionCountByIdentity(OdinId odinId, Guid fileId)
    {
        using var cn = tenantSystemStorage.CreateConnection();
        return _db.tblDriveReactions.GetIdentityPostReactions(cn, odinId, Drive.Id, fileId);
    }

    public (List<Reaction>, Int32? cursor) GetReactionsByFile(int maxCount, int cursor, Guid fileId)
    {
        using var cn = tenantSystemStorage.CreateConnection();
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

    public Task<(Int64 fileCount, Int64 byteSize)> GetDriveSizeInfo()
    {
        using var cn = tenantSystemStorage.CreateConnection();
        var (count, size) = _db.tblDriveMainIndex.GetDriveSize(cn, Drive.Id);
        return Task.FromResult((count, size));
    }
    
    private Task<(QueryBatchCursor cursor, IEnumerable<Guid> fileIds, bool hasMoreRows)> GetBatchExplicitOrdering(IOdinContext odinContext,
        FileSystemType fileSystemType, FileQueryParams qp, QueryBatchResultOptions options)
    {
        var securityRange = new IntRange(0, (int)odinContext.Caller.SecurityLevel);

        var aclList = GetAcl(odinContext);

        var cursor = options.Cursor;

        using var cn = tenantSystemStorage.CreateConnection();
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