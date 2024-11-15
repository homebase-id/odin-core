using System.Collections.Generic;
using System.Threading.Tasks;
using MediatR;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage.SQLite;
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
    public async Task AddReactionAsync(InternalDriveFileId file, string reactionContent, OdinId senderId, IOdinContext odinContext, IdentityDatabase db)
    {
        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.React);

        var manager = await driveDatabaseHost.TryGetOrLoadQueryManagerAsync(file.DriveId, db);
        if (manager != null)
        {
            await manager.AddReactionAsync(senderId, file.FileId, reactionContent, db);
            
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

    public async Task DeleteReactionAsync(InternalDriveFileId file, string reactionContent, OdinId senderId, IOdinContext odinContext, IdentityDatabase db)
    {
        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.React);

        var manager = await driveDatabaseHost.TryGetOrLoadQueryManagerAsync(file.DriveId, db);
        if (manager != null)
        {
            await manager.DeleteReactionAsync(senderId, file.FileId, reactionContent, db);

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

    public async Task<GetReactionCountsResponse> GetReactionCountsByFileAsync(InternalDriveFileId file, IOdinContext odinContext, IdentityDatabase db)
    {
        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);

        var manager = await driveDatabaseHost.TryGetOrLoadQueryManagerAsync(file.DriveId, db);
        if (manager != null)
        {
            var (reactions, total) = await manager.GetReactionSummaryByFileAsync(file.FileId, db);

            return new GetReactionCountsResponse()
            {
                Reactions = reactions,
                Total = total
            };
        }

        throw new OdinSystemException($"Invalid query manager instance for drive {file.DriveId}");
    }

    public async Task<List<string>> GetReactionsByIdentityAndFileAsync(OdinId identity, InternalDriveFileId file, IOdinContext odinContext, IdentityDatabase db)
    {
        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);

        var manager = await driveDatabaseHost.TryGetOrLoadQueryManagerAsync(file.DriveId, db);
        if (manager != null)
        {
            var result = await manager.GetReactionsByIdentityAndFileAsync(identity, file.FileId, db);
            return result;
        }

        throw new OdinSystemException($"Invalid query manager instance for drive {file.DriveId}");
    }

    public async Task DeleteAllReactionsAsync(InternalDriveFileId file, IOdinContext odinContext, IdentityDatabase db)
    {
        var context = odinContext;
        context.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.React);

        var manager = await driveDatabaseHost.TryGetOrLoadQueryManagerAsync(file.DriveId, db);
        if (manager != null)
        {
            await manager.DeleteReactionsAsync(context.GetCallerOdinIdOrFail(), file.FileId, db);

            await mediator.Publish(new AllReactionsByFileDeleted
            {
                FileId = file,
                OdinContext = odinContext,
                db = db
            });
        }
    }

    public async Task<GetReactionsResponse> GetReactionsAsync(InternalDriveFileId file, int cursor, int maxCount, IOdinContext odinContext, IdentityDatabase db)
    {
        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);

        var manager = await driveDatabaseHost.TryGetOrLoadQueryManagerAsync(file.DriveId, db);
        if (manager != null)
        {
            var (list, nextCursor) =
                await manager.GetReactionsByFileAsync(maxCount, cursor, file.FileId, db);

            return new GetReactionsResponse()
            {
                Reactions = list,
                Cursor = nextCursor
            };
        }

        throw new OdinSystemException($"Invalid query manager instance for drive {file.DriveId}");
    }
}