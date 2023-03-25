using System.Collections.Generic;
using System.Threading.Tasks;
using MediatR;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives.DriveCore.Query.Sqlite;

namespace Youverse.Core.Services.Drives.Reactions;

//TODO: need to determine if I want to validate if the file exists.  file exist calls are expensive 

/// <summary>
/// Manages emoji reactions to files
/// </summary>
public class EmojiReactionService
{
    private readonly DotYouContextAccessor _contextAccessor;
    private readonly DriveDatabaseHost _driveDatabaseHost;
    private readonly IMediator _mediator;

    public EmojiReactionService(DriveDatabaseHost driveDatabaseHost, DotYouContextAccessor contextAccessor, IMediator mediator)
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

        _mediator.Publish(new EmojiReactionAddedNotification()
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

        throw new YouverseSystemException($"Invalid query manager instance for drive {file.DriveId}");
    }

    public List<string> GetReactionsByIdentityAndFile(OdinId identity, InternalDriveFileId file)
    {
        _contextAccessor.GetCurrent().PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);

        if (_driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId, out var manager))
        {
            var result = manager.GetReactionsByIdentityAndFile(identity, file.FileId);
            return result;
        }

        throw new YouverseSystemException($"Invalid query manager instance for drive {file.DriveId}");
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

        throw new YouverseSystemException($"Invalid query manager instance for drive {file.DriveId}");
    }
}