using System.Collections.Generic;
using System.Threading.Tasks;
using MediatR;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Query.Sqlite;

namespace Odin.Services.Drives.Reactions;

//TODO: include checks to ensure the file exists

/// <summary>
/// Manages reactions to files
/// </summary>
public class ReactionContentService(DriveDatabaseHost driveDatabaseHost, IMediator mediator)
{
    public async Task AddReaction(InternalDriveFileId file, string reactionContent, OdinId senderId, IOdinContext odinContext, IdentityDatabase db)
    {
        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.React);

        var manager = await driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId, db);
        if (manager != null)
        {
            manager.AddReaction(senderId, file.FileId, reactionContent, db);

            await mediator.Publish(new ReactionContentAddedNotification
            {
                Reaction = new Reaction()
                {
                    OdinId = senderId,
                    Created = UnixTimeUtcUnique.Now(), //TODO: i should technically pull this from the db records
                    ReactionContent = reactionContent,
                    FileId = file
                },
                OdinContext = odinContext,
                db = db
            });
        }
    }

    public async Task DeleteReaction(InternalDriveFileId file, string reactionContent, OdinId senderId, IOdinContext odinContext, IdentityDatabase db)
    {
        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.React);

        var manager = await driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId, db);
        if (manager != null)
        {
            manager.DeleteReaction(senderId, file.FileId, reactionContent, db);

            await mediator.Publish(new ReactionContentDeletedNotification
            {
                Reaction = new Reaction()
                {
                    OdinId = senderId,
                    Created = default,
                    ReactionContent = reactionContent,
                    FileId = file
                },
                OdinContext = odinContext,
                db = db
            });
        }
    }

    public async Task<GetReactionCountsResponse> GetReactionCountsByFile(InternalDriveFileId file, IOdinContext odinContext, IdentityDatabase db)
    {
        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);

        var manager = await driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId, db);
        if (manager != null)
        {
            var (reactions, total) = manager.GetReactionSummaryByFile(file.FileId, db);

            return new GetReactionCountsResponse()
            {
                Reactions = reactions,
                Total = total
            };
        }

        throw new OdinSystemException($"Invalid query manager instance for drive {file.DriveId}");
    }

    public async Task<List<string>> GetReactionsByIdentityAndFile(OdinId identity, InternalDriveFileId file, IOdinContext odinContext, IdentityDatabase db)
    {
        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);

        var manager = await driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId, db);
        if (manager != null)
        {
            var result = manager.GetReactionsByIdentityAndFile(identity, file.FileId, db);
            return result;
        }

        throw new OdinSystemException($"Invalid query manager instance for drive {file.DriveId}");
    }

    public async Task DeleteAllReactions(InternalDriveFileId file, IOdinContext odinContext, IdentityDatabase db)
    {
        var context = odinContext;
        context.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.React);

        var manager = await driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId, db);
        if (manager != null)
        {
            manager.DeleteReactions(context.GetCallerOdinIdOrFail(), file.FileId, db);

            await mediator.Publish(new AllReactionsByFileDeleted
            {
                FileId = file,
                OdinContext = odinContext,
                db = db
            });
        }
    }

    public async Task<GetReactionsResponse> GetReactions(InternalDriveFileId file, int cursor, int maxCount, IOdinContext odinContext, IdentityDatabase db)
    {
        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);

        var manager = await driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId, db);
        if (manager != null)
        {
            var (list, nextCursor) =
                manager.GetReactionsByFile(maxCount, cursor, file.FileId, db);

            return new GetReactionsResponse()
            {
                Reactions = list,
                Cursor = nextCursor
            };
        }

        throw new OdinSystemException($"Invalid query manager instance for drive {file.DriveId}");
    }
}