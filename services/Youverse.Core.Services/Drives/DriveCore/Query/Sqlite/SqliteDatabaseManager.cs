using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives.DriveCore.Storage;
using Youverse.Core.Storage;
using Youverse.Core.Storage.Sqlite.DriveDatabase;

namespace Youverse.Core.Services.Drives.DriveCore.Query.Sqlite;

public class SqliteDatabaseManager : IDriveDatabaseManager
{
    private readonly ILogger<object> _logger;

    private readonly DriveDatabase _db;

    public SqliteDatabaseManager(StorageDrive drive, ILogger<object> logger)
    {
        Drive = drive;
        _logger = logger;

        var connectionString = $"Data Source={drive.GetIndexPath()}/index.db";
        _db = new DriveDatabase(connectionString, DatabaseIndexKind.TimeSeries);
    }

    public StorageDrive Drive { get; init; }

    public Task<(long, IEnumerable<Guid>)> GetModified(DotYouContext dotYouContext, FileSystemType fileSystemType,
        FileQueryParams qp, QueryModifiedResultOptions options)
    {
        Guard.Argument(dotYouContext, nameof(dotYouContext)).NotNull();
        var callerContext = dotYouContext.Caller;

        var requiredSecurityGroup = new IntRange(0, (int)callerContext.SecurityLevel);
        var aclList = GetAcl(dotYouContext);
        var cursor = new UnixTimeUtcUnique(options.Cursor);

        // TODO TODD - use moreRows
        var (results, moreRows) = _db.QueryModified(
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
            archivalStatusAnyOf: qp.ArchivalStatus.HasValue ? new List<int>() { qp.ArchivalStatus.Value } : null);

        return Task.FromResult((cursor.uniqueTime, results.AsEnumerable()));
    }


    public Task<(QueryBatchCursor, IEnumerable<Guid>, bool? hasMoreRows)> GetBatch(DotYouContext dotYouContext,
        FileSystemType fileSystemType, FileQueryParams qp, QueryBatchResultOptions options)
    {
        Guard.Argument(dotYouContext, nameof(dotYouContext)).NotNull();

        var securityRange = new IntRange(0, (int)dotYouContext.Caller.SecurityLevel);
        var aclList = GetAcl(dotYouContext);
        var cursor = options.Cursor;

        if (options.Ordering == Ordering.Default)
        {
            // TODO TODD use moreRows
            var (results, moreRows) = _db.QueryBatchAuto(
                noOfItems: options.MaxRecords,
                cursor: ref cursor,
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
                archivalStatusAnyOf: qp.ArchivalStatus.HasValue ? new List<int>() { qp.ArchivalStatus.Value } : null);

            return Task.FromResult((cursor, results.Select(r => r), (bool?)null));
        }

        // if the caller was explicit in how they want results...
        return GetBatchExplicitOrdering(dotYouContext, fileSystemType, qp, options);
    }

    private List<Guid> GetAcl(DotYouContext dotYouContext)
    {
        var callerContext = dotYouContext.Caller;

        var aclList = new List<Guid>();
        if (callerContext.IsOwner == false)
        {
            if (!callerContext.IsAnonymous)
            {
                aclList.Add(dotYouContext.GetCallerOdinIdOrFail().ToHashId());
            }

            aclList.AddRange(callerContext.Circles?.Select(c => c.Value) ?? Array.Empty<Guid>());
        }

        return aclList.Any() ? aclList : null;
    }

    public Task UpdateCurrentIndex(ServerFileHeader header)
    {
        var metadata = header.FileMetadata;

        int securityGroup = (int)header.ServerMetadata.AccessControlList.RequiredSecurityGroup;
        var exists = _db.TblMainIndex.Get(metadata.File.FileId) != null;

        if (header.ServerMetadata.DoNotIndex)
        {
            if (exists) // clean up if the flag was changed after it was indexed
            {
                _db.TblMainIndex.Delete(metadata.File.FileId);
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
                fileSystemType: (int)header.ServerMetadata.FileSystemType);
        }
        else
        {
            _db.AddEntry(
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
                (int)header.ServerMetadata.FileSystemType
            );
        }

        return Task.CompletedTask;
    }

    public Task RemoveFromCurrentIndex(InternalDriveFileId file)
    {
        _db.DeleteEntry(file.FileId);
        return Task.CompletedTask;
    }

    public Task LoadLatestIndex()
    {
        _db.CreateDatabase(false);
        return Task.CompletedTask;
    }

    public Task AddCommandMessage(List<Guid> fileIds)
    {
        _db.TblCmdMsgQueue.InsertRows(fileIds);
        return Task.CompletedTask;
    }

    public Task<List<UnprocessedCommandMessage>> GetUnprocessedCommands(int count)
    {
        Guard.Argument(count, nameof(count)).Require(c => c > 0);
        var list = _db.TblCmdMsgQueue.Get(count) ?? new List<CommandMessageQueueRecord>();

        var result = list.Select(x => new UnprocessedCommandMessage()
        {
            Id = x.fileId,
            Received = x.timeStamp
        }).ToList();

        return Task.FromResult(result);
    }

    public Task MarkCommandsCompleted(List<Guid> fileIds)
    {
        _db.TblCmdMsgQueue.DeleteRow(fileIds);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _db.Commit();
        _db.Dispose();
    }

    public void AddReaction(OdinId odinId, Guid fileId, string reaction)
    {
        _db.TblReactions.Insert(new ReactionsRecord()
            { identity = odinId, postId = fileId, singleReaction = reaction });
    }

    public void DeleteReactions(OdinId odinId, Guid fileId)
    {
        _db.TblReactions.DeleteAllReactions(odinId, fileId);
    }

    public void DeleteReaction(OdinId odinId, Guid fileId, string reaction)
    {
        _db.TblReactions.Delete(odinId, fileId, reaction);
    }

    public (List<string>, int) GetReactions(Guid fileId)
    {
        return _db.TblReactions.GetPostReactions(fileId);
    }

    public (List<ReactionCount> reactions, int total) GetReactionSummaryByFile(Guid fileId)
    {
        var (reactionContentList, countByReactionsList, total) = _db.TblReactions.GetPostReactionsWithDetails(fileId);

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
        return _db.TblReactions.GetIdentityPostReactionDetails(identity, fileId);
    }

    public int GetReactionCountByIdentity(OdinId odinId, Guid fileId)
    {
        return _db.TblReactions.GetIdentityPostReactions(odinId, fileId);
    }

    public (List<Reaction>, Int32? cursor) GetReactionsByFile(int maxCount, int cursor, Guid fileId)
    {
        var items = _db.TblReactions.PagingByRowid(maxCount, inCursor: cursor, out var nextCursor, fileId);

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

    private Task<(QueryBatchCursor cursor, IEnumerable<Guid> fileIds, bool? hasMoreRows)> GetBatchExplicitOrdering(DotYouContext dotYouContext,
        FileSystemType fileSystemType, FileQueryParams qp, QueryBatchResultOptions options)
    {
        Guard.Argument(dotYouContext, nameof(dotYouContext)).NotNull();

        var securityRange = new IntRange(0, (int)dotYouContext.Caller.SecurityLevel);

        var aclList = GetAcl(dotYouContext);

        var cursor = options.Cursor;

        var (results, hasMoreRows) = _db.QueryBatch(
            noOfItems: options.MaxRecords,
            cursor: ref cursor,
            newestFirstOrder: options.Ordering == Ordering.NewestFirst,
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
            archivalStatusAnyOf: qp.ArchivalStatus.HasValue ? new List<int>() { qp.ArchivalStatus.Value } : null);

        return Task.FromResult((cursor, results.Select(r => r), (bool?)hasMoreRows));
    }
}

public class ReactionCount
{
    public string ReactionContent { get; set; }
    public int Count { get; set; }
}