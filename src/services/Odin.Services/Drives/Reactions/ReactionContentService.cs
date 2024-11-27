using System.Collections.Generic;
using System.Threading.Tasks;
using MediatR;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage.SQLite;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Query.Sqlite;
using Odin.Services.Drives.Management;

namespace Odin.Services.Drives.Reactions;

//TODO: include checks to ensure the file exists

/// <summary>
/// Manages reactions to files
/// </summary>
public class ReactionContentService(DriveManager driveManager, SqliteDatabaseManager driveQuery, IMediator mediator)
{
    public async Task AddReactionAsync(InternalDriveFileId file, string reactionContent, OdinId senderId, IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.React);

        var drive = await driveManager.GetDriveAsync(file.DriveId, failIfInvalid: true);
        await driveQuery.AddReactionAsync(drive, senderId, file.FileId, reactionContent);

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
        });
    }

    public async Task DeleteReactionAsync(InternalDriveFileId file, string reactionContent, OdinId senderId, IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.React);

        var drive = await driveManager.GetDriveAsync(file.DriveId, failIfInvalid: true);
        await driveQuery.DeleteReactionAsync(drive, senderId, file.FileId, reactionContent);

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
        });
    }

    public async Task<GetReactionCountsResponse> GetReactionCountsByFileAsync(InternalDriveFileId file, IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);

        var drive = await driveManager.GetDriveAsync(file.DriveId, failIfInvalid: true);
        var (reactions, total) = await driveQuery.GetReactionSummaryByFileAsync(drive, file.FileId);

        return new GetReactionCountsResponse()
        {
            Reactions = reactions,
            Total = total
        };
    }

    public async Task<List<string>> GetReactionsByIdentityAndFileAsync(OdinId identity, InternalDriveFileId file, IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);

        var drive = await driveManager.GetDriveAsync(file.DriveId, failIfInvalid: true);
        return await driveQuery.GetReactionsByIdentityAndFileAsync(drive, identity, file.FileId);
    }

    public async Task DeleteAllReactionsAsync(InternalDriveFileId file, IOdinContext odinContext)
    {
        var context = odinContext;
        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.React);

        var drive = await driveManager.GetDriveAsync(file.DriveId, failIfInvalid: true);
        await driveQuery.DeleteReactionsAsync(drive, context.GetCallerOdinIdOrFail(), file.FileId);

        await mediator.Publish(new AllReactionsByFileDeleted
        {
            FileId = file,
            OdinContext = odinContext,
        });
    }

    public async Task<GetReactionsResponse> GetReactionsAsync(InternalDriveFileId file, int cursor, int maxCount, IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);

        var drive = await driveManager.GetDriveAsync(file.DriveId, failIfInvalid: true);
        var (list, nextCursor) = await driveQuery.GetReactionsByFileAsync(drive, maxCount, cursor, file.FileId);

        return new GetReactionsResponse()
        {
            Reactions = list,
            Cursor = nextCursor
        };
    }
}
