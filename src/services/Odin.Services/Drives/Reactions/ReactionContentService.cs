using System.Collections.Generic;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage.SQLite;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Query.Sqlite;

namespace Odin.Services.Drives.Reactions;

//TODO: need to determine if I want to validate if the file exists.  file exist calls are expensive. (SEB:No they are not.)

/// <summary>
/// Manages reactions to files
/// </summary>
public class ReactionContentService(
    ILogger<ReactionContentService> logger,
    DriveDatabaseHost driveDatabaseHost,
    IMediator mediator)
{
    public async Task AddReaction(InternalDriveFileId file, string reactionContent, IOdinContext odinContext, DatabaseConnection cn)
    {
        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.React);
        var callerId = odinContext.GetCallerOdinIdOrFail();

        var manager = await driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId, cn);
        if (manager != null && manager.AddReaction(callerId, file.FileId, reactionContent, cn))
        {
            logger.LogDebug("{CallerId} added reaction {ReactionContent} to file {FileId}", callerId, reactionContent, file.FileId);

            await mediator.Publish(new ReactionContentAddedNotification
            {
                Reaction = new Reaction()
                {
                    OdinId = callerId,
                    Created = UnixTimeUtcUnique.Now(), //TODO: i should technically pull this from the db records
                    ReactionContent = reactionContent,
                    FileId = file
                },
                OdinContext = odinContext,
                DatabaseConnection = cn
            });
        }
    }

    //

    public async Task DeleteReaction(InternalDriveFileId file, string reactionContent, IOdinContext odinContext, DatabaseConnection cn)
    {
        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.React);
        var callerId = odinContext.GetCallerOdinIdOrFail();

        var manager = await driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId, cn);
        if (manager != null && manager.DeleteReaction(callerId, file.FileId, reactionContent, cn))
        {
            logger.LogDebug("{CallerId} deleted reaction {ReactionContent} to file {FileId}", callerId, reactionContent, file.FileId);

            await mediator.Publish(new ReactionDeletedNotification
            {
                Reaction = new Reaction
                {
                    OdinId = callerId,
                    Created = default,
                    ReactionContent = reactionContent,
                    FileId = file
                },
                OdinContext = odinContext,
                DatabaseConnection = cn
            });
        }
    }

    //

    public async Task<GetReactionCountsResponse> GetReactionCountsByFile(InternalDriveFileId file, IOdinContext odinContext, DatabaseConnection cn)
    {
        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);

        var manager = await driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId, cn);
        if (manager != null)
        {
            var (reactions, total) = manager.GetReactionSummaryByFile(file.FileId, cn);

            return new GetReactionCountsResponse()
            {
                Reactions = reactions,
                Total = total
            };
        }

        throw new OdinSystemException($"Invalid query manager instance for drive {file.DriveId}");
    }

    //

    public async Task<List<string>> GetReactionsByIdentityAndFile(OdinId identity, InternalDriveFileId file, IOdinContext odinContext, DatabaseConnection cn)
    {
        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);

        var manager = await driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId, cn);
        if (manager != null)
        {
            var result = manager.GetReactionsByIdentityAndFile(identity, file.FileId, cn);
            return result;
        }

        throw new OdinSystemException($"Invalid query manager instance for drive {file.DriveId}");
    }

    //

    public async Task DeleteAllReactions(InternalDriveFileId file, IOdinContext odinContext, DatabaseConnection cn)
    {
        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.React);
        var callerId = odinContext.GetCallerOdinIdOrFail();

        var manager = await driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId, cn);
        if (manager != null && manager.DeleteReactions(callerId, file.FileId, cn))
        {
            await mediator.Publish(new AllReactionsByFileDeleted
            {
                FileId = file,
                OdinContext = odinContext,
                DatabaseConnection = cn
            });
        }
    }

    //

    public async Task<GetReactionsResponse> GetReactions(InternalDriveFileId file, int cursor, int maxCount, IOdinContext odinContext, DatabaseConnection cn)
    {
        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);

        var manager = await driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId, cn);
        if (manager != null)
        {
            var (list, nextCursor) =
                manager.GetReactionsByFile(maxCount, cursor, file.FileId, cn);

            return new GetReactionsResponse()
            {
                Reactions = list,
                Cursor = nextCursor
            };
        }

        throw new OdinSystemException($"Invalid query manager instance for drive {file.DriveId}");
    }
}