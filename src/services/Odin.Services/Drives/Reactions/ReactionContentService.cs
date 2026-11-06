using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Time;
using Odin.Services.Authorization.Acl;
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
    IDriveAclAuthorizationService driveAclAuthorizationService,
    IMediator mediator)
{
    /// <summary>
    /// Whether the caller may <paramref name="permission"/> (React, or Read to see the reactions) on <paramref name="file"/>,
    /// by the one rule for every drive (<see cref="IDriveAclAuthorizationService.CallerMayAsync"/>): the bit on the drive,
    /// and on a Private Share drive a live share with it too, so whoever may read a file sees its reactions, and a share's
    /// <c>React</c> bit lets them react.  A comment goes by its file's shares.
    /// </summary>
    public async Task<StorageDrive> AssertMayAsync(InternalDriveFileId file, DrivePermission permission, IOdinContext odinContext)
    {
        var drive = await driveManager.GetDriveAsync(file.DriveId, failIfInvalid: true);
        var header = SelectiveShareDrives.RowsDecideFor(drive, odinContext.Caller) ? await driveQuery.GetFileHeaderAsync(drive, file.FileId) : null;
        await driveAclAuthorizationService.AssertCallerMayAsync(drive, header, permission, odinContext);
        return drive;
    }

    public async Task<bool> AddReactionAsync(InternalDriveFileId file, string reactionContent, OdinId senderId, IOdinContext odinContext,
        WriteSecondDatabaseRowBase markComplete)
    {
        var drive = await AssertMayAsync(file, DrivePermission.React, odinContext);
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

    public async Task<bool> DeleteReactionAsync(InternalDriveFileId file, string reactionContent, OdinId senderId, IOdinContext odinContext,
        WriteSecondDatabaseRowBase markComplete)
    {
        var drive = await AssertMayAsync(file, DrivePermission.React, odinContext);

        await using (var tx = await database.BeginStackedTransactionAsync())
        {
            await driveQuery.DeleteReactionAsync(drive, senderId, file.FileId, reactionContent);
            // We could check here if 1 row was deleted ...

            logger.LogDebug("{method} -> markComplete {message}",
                nameof(DeleteReactionAsync),
                markComplete == null ? "is not configured" : "will be called");

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
        var drive = await AssertMayAsync(file, DrivePermission.Read, odinContext);
        var (reactions, total) = await driveQuery.GetReactionSummaryByFileAsync(drive, file.FileId);

        return new GetReactionCountsResponse()
        {
            Reactions = reactions,
            Total = total
        };
    }

    public async Task<List<string>> GetReactionsByIdentityAndFileAsync(OdinId identity, InternalDriveFileId file, IOdinContext odinContext)
    {
        var drive = await AssertMayAsync(file, DrivePermission.Read, odinContext);
        return await driveQuery.GetReactionsByIdentityAndFileAsync(drive.Id, identity, file.FileId);
    }

    public async Task DeleteAllReactionsAsync(InternalDriveFileId file, IOdinContext odinContext)
    {
        var context = odinContext;
        var drive = await AssertMayAsync(file, DrivePermission.React, odinContext);
        await driveQuery.DeleteReactionsAsync(drive, context.GetCallerOdinIdOrFail(), file.FileId);

        await mediator.Publish(new AllReactionsByFileDeleted
        {
            FileId = file,
            OdinContext = odinContext,
        });
    }

    public async Task<GetReactionsResponse> GetReactionsAsync(InternalDriveFileId file, int cursor, int maxCount, IOdinContext odinContext)
    {
        var drive = await AssertMayAsync(file, DrivePermission.Read, odinContext);
        var (list, nextCursor) = await driveQuery.GetReactionsByFileAsync(drive, maxCount, cursor, file.FileId);

        return new GetReactionsResponse()
        {
            Reactions = list,
            Cursor = nextCursor
        };
    }
}