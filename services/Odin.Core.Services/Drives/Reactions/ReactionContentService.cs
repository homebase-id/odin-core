using System.Collections.Generic;
using MediatR;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives.DriveCore.Query.Sqlite;
using Odin.Core.Time;

namespace Odin.Core.Services.Drives.Reactions;

//TODO: need to determine if I want to validate if the file exists.  file exist calls are expensive 

/// <summary>
/// Manages reactions to files
/// </summary>
public class ReactionContentService
{
    private readonly OdinContextAccessor _contextAccessor;
    private readonly DriveDatabaseHost _driveDatabaseHost;
    private readonly IMediator _mediator;

    public ReactionContentService(DriveDatabaseHost driveDatabaseHost, OdinContextAccessor contextAccessor, IMediator mediator)
    {
        _driveDatabaseHost = driveDatabaseHost;
        _contextAccessor = contextAccessor;
        _mediator = mediator;
    }

    public void AddReaction(InternalDriveFileId file, string reactionContent)
    {
        var context = _contextAccessor.GetCurrent();
        context.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.WriteReactionsAndComments);
        var callerId = context.GetCallerOdinIdOrFail();
        if (_driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId, out var manager))
        {
            manager.AddReaction(callerId, file.FileId, reactionContent);
        }

        _mediator.Publish(new ReactionContentAddedNotification()
        {
            Reaction = new Reaction()
            {
                OdinId = callerId,
                Created = UnixTimeUtcUnique.Now(), //TODO: i should technically pull this from the db records
                ReactionContent = reactionContent,
                FileId = file
            }
        });
    }

    public void DeleteReaction(InternalDriveFileId file, string reactionContent)
    {
        var context = _contextAccessor.GetCurrent();
        context.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.WriteReactionsAndComments);

        if (_driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId, out var manager))
        {
            manager.DeleteReaction(context.GetCallerOdinIdOrFail(), file.FileId, reactionContent);
        }
    }

    public GetReactionCountsResponse GetReactionCountsByFile(InternalDriveFileId file)
    {
        _contextAccessor.GetCurrent().PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);

        if (_driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId, out var manager))
        {
            var (reactions, total) = manager.GetReactionSummaryByFile(file.FileId);

            return new GetReactionCountsResponse()
            {
                Reactions = reactions,
                Total = total
            };
        }

        throw new OdinSystemException($"Invalid query manager instance for drive {file.DriveId}");
    }

    public List<string> GetReactionsByIdentityAndFile(OdinId identity, InternalDriveFileId file)
    {
        _contextAccessor.GetCurrent().PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);

        if (_driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId, out var manager))
        {
            var result = manager.GetReactionsByIdentityAndFile(identity, file.FileId);
            return result;
        }

        throw new OdinSystemException($"Invalid query manager instance for drive {file.DriveId}");
    }

    public void DeleteAllReactions(InternalDriveFileId file)
    {
        var context = _contextAccessor.GetCurrent();
        context.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.WriteReactionsAndComments);

        if (_driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId, out var manager))
        {
            manager.DeleteReactions(context.GetCallerOdinIdOrFail(), file.FileId);
        }
    }
    
    public GetReactionsResponse GetReactions(InternalDriveFileId file, int cursor, int maxCount)
    {
        _contextAccessor.GetCurrent().PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);

        if (_driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId, out var manager))
        {
            var (list, nextCursor) =
                manager.GetReactionsByFile(fileId: file.FileId, cursor: cursor, maxCount: maxCount);

            return new GetReactionsResponse()
            {
                Reactions = list,
                Cursor = nextCursor
            };
        }

        throw new OdinSystemException($"Invalid query manager instance for drive {file.DriveId}");
    }
}