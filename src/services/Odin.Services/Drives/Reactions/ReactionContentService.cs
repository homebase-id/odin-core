using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.Management;
using Odin.Services.Peer.Incoming.Drive.Transfer;

namespace Odin.Services.Drives.Reactions;

//TODO: include checks to ensure the file exists


/// <summary>
/// Manages reactions to files
/// </summary>
public class ReactionContentService(
    ILogger<ReactionContentService> logger,
    IdentityDatabase database,
    IDriveManager driveManager,
    DriveQuery driveQuery,
    IMediator mediator)
{
    public async Task<bool> AddReactionAsync(InternalDriveFileId file, string reactionContent, OdinId senderId, IOdinContext odinContext, WriteSecondDatabaseRowBase markComplete)
    {
        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.React);

        var drive = await driveManager.GetDriveAsync(file.DriveId, failIfInvalid: true);
        await driveQuery.AddReactionAsync(drive, senderId, file.FileId, reactionContent, markComplete);

        try
        {
            // Maybe we can extend mediator with a TryPublish - that'd be nice.
            await mediator.Publish(new ReactionContentAddedNotification
            {
                Reaction = new Reaction()
                {
                    OdinId = senderId,
                    Created = UnixTimeUtc.Now(), //TODO: i should technically pull this from the db records
                    ReactionContent = reactionContent,
                    FileId = file
                },
                OdinContext = odinContext,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "mediator.Publish threw an error");
        }


        return true;
    }

    public async Task<bool> DeleteReactionAsync(InternalDriveFileId file, string reactionContent, OdinId senderId, IOdinContext odinContext, WriteSecondDatabaseRowBase markComplete)
    {
        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.React);

        var drive = await driveManager.GetDriveAsync(file.DriveId, failIfInvalid: true);

        await using (var tx = await database.BeginStackedTransactionAsync())
        {
            await driveQuery.DeleteReactionAsync(drive, senderId, file.FileId, reactionContent);
            // We could check here if 1 row was deleted ...

            if (markComplete != null)
            {
                var n = await markComplete.ExecuteAsync();

                if (n != 1)
                    throw new OdinSystemException("Hum, unable to mark the inbox record as completed, aborting");
            }
            tx.Commit();
        }


        // Want a TryPublish ... maybe extend the lib?
        try
        {
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
        catch (Exception ex) 
        {
            logger.LogError(ex, "mediator.Publish threw an error");
        }

        return true;
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
