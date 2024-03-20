using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Query.Sqlite;
using Odin.Services.Drives.Management;
using Odin.Services.Peer.Outgoing.Drive.Reactions;

namespace Odin.Services.Drives.Reactions;

/// <summary>
/// Manages reactions to files
/// </summary>
public class ReactionContentService(
    DriveDatabaseHost driveDatabaseHost,
    OdinContextAccessor contextAccessor,
    IMediator mediator,
    ILogger<ReactionContentService> logger,
    PeerReactionSenderService reactionSenderService,
    DriveManager driveManager,
    FileSystemResolver fileSystemResolver)
{
    public async Task<AddGroupReactionResponse> AddGroupReaction(InternalDriveFileId internalFile, IEnumerable<OdinId> recipients, string reaction)
    {
        var fs = await fileSystemResolver.ResolveFileSystem(internalFile);
        var header = await fs.Storage.GetServerFileHeader(internalFile);

        if (header == null)
        {
            throw new OdinClientException("File does not exist");
        }

        if (header.FileMetadata.GlobalTransitId == null)
        {
            throw new OdinClientException("File must have a global transit id when adding a group reaction");
        }

        // add a local reaction
        await AddReaction(internalFile, reaction);

        var remoteRequest = new AddRemoteReactionRequest()
        {
            File = new GlobalTransitIdFileIdentifier()
            {
                GlobalTransitId = header.FileMetadata.GlobalTransitId.GetValueOrDefault(),
                TargetDrive = (await driveManager.GetDrive(internalFile.DriveId)).TargetDriveInfo
            },
            Reaction = reaction
        };

        // Broadcast to recipients
        var response = new AddGroupReactionResponse();
        var tasks = new List<Task<(OdinId recipient, AddDeleteRemoteReactionStatusCode code)>>();
        var odinIds = recipients as OdinId[] ?? recipients.ToArray();
        tasks.AddRange(odinIds.Select(id => SendReactionInternal(id, remoteRequest)));
        await Task.WhenAll(tasks);

        tasks.ForEach(task =>
        {
            var sendResponse = task.Result;
            response.Responses.Add(new AddDeleteRemoteReactionResponse()
            {
                Recipient = sendResponse.recipient,
                Status = sendResponse.code
            });
        });

        return response;

        async Task<(OdinId recipient, AddDeleteRemoteReactionStatusCode code)> SendReactionInternal(OdinId odinId, AddRemoteReactionRequest request)
        {
            var code = AddDeleteRemoteReactionStatusCode.Failure;
            try
            {
                code = await reactionSenderService.SendReaction(odinId, request);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed sending reaction to recipient {recipient}", odinId);
            }

            return (odinId, code);
        }
    }

    public async Task<DeleteGroupReactionResponse> DeleteGroupReaction(InternalDriveFileId internalFile, IEnumerable<OdinId> recipients, string reaction)
    {
        var fs = await fileSystemResolver.ResolveFileSystem(internalFile);
        var header = await fs.Storage.GetServerFileHeader(internalFile);

        if (header == null)
        {
            throw new OdinClientException("File does not exist");
        }

        if (header.FileMetadata.GlobalTransitId == null)
        {
            throw new OdinClientException("File must be global transit id when adding a group reaction");
        }

        // add a local reaction
        await DeleteReaction(internalFile, reaction);

        //broadcast to recipients
        var response = new DeleteGroupReactionResponse();
        var tasks = new List<Task<(OdinId recipient, AddDeleteRemoteReactionStatusCode code)>>();
        var odinIds = recipients as OdinId[] ?? recipients.ToArray();
        var remoteRequest = new DeleteReactionRequestByGlobalTransitId()
        {
            File = new GlobalTransitIdFileIdentifier()
            {
                GlobalTransitId = header.FileMetadata.GlobalTransitId.GetValueOrDefault(),
                TargetDrive = (await driveManager.GetDrive(internalFile.DriveId)).TargetDriveInfo
            },
            Reaction = reaction
        };

        tasks.AddRange(odinIds.Select(id => DeleteReactionInternal(id, remoteRequest)));
        await Task.WhenAll(tasks);

        tasks.ForEach(task =>
        {
            var sendResponse = task.Result;
            response.Responses.Add(new AddDeleteRemoteReactionResponse()
            {
                Recipient = sendResponse.recipient,
                Status = sendResponse.code
            });
        });

        return response;
        
        async Task<(OdinId recipient, AddDeleteRemoteReactionStatusCode)> DeleteReactionInternal(OdinId odinId, DeleteReactionRequestByGlobalTransitId request)
        {
            try
            {
                var responseCode = await reactionSenderService.DeleteReaction(odinId, request);
                return (odinId, responseCode);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed deleting reaction from recipient {recipient}", odinId);
                return (odinId, AddDeleteRemoteReactionStatusCode.Failure);
            }
        }
    }

    public async Task AddReaction(InternalDriveFileId file, string reactionContent)
    {
        var context = contextAccessor.GetCurrent();
        context.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.React);
        var callerId = context.GetCallerOdinIdOrFail();

        var manager = await driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId);
        if (manager != null)
        {
            manager.AddReaction(callerId, file.FileId, reactionContent);

            await mediator.Publish(new ReactionContentAddedNotification()
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
    }

    public async Task DeleteReaction(InternalDriveFileId file, string reactionContent)
    {
        var context = contextAccessor.GetCurrent();
        context.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.React);

        var manager = await driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId);
        if (manager != null)
        {
            manager.DeleteReaction(context.GetCallerOdinIdOrFail(), file.FileId, reactionContent);

            await mediator.Publish(new ReactionDeletedNotification()
            {
                Reaction = new Reaction()
                {
                    OdinId = context.GetCallerOdinIdOrFail(),
                    Created = default,
                    ReactionContent = reactionContent,
                    FileId = file
                }
            });
        }
    }

    public async Task<GetReactionCountsResponse> GetReactionCountsByFile(InternalDriveFileId file)
    {
        contextAccessor.GetCurrent().PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);

        var manager = await driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId);
        if (manager != null)
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

    public async Task<List<string>> GetReactionsByIdentityAndFile(OdinId identity, InternalDriveFileId file)
    {
        contextAccessor.GetCurrent().PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);

        var manager = await driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId);
        if (manager != null)
        {
            var result = manager.GetReactionsByIdentityAndFile(identity, file.FileId);
            return result;
        }

        throw new OdinSystemException($"Invalid query manager instance for drive {file.DriveId}");
    }

    public async Task DeleteAllReactions(InternalDriveFileId file)
    {
        var context = contextAccessor.GetCurrent();
        context.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.React);

        var manager = await driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId);
        if (manager != null)
        {
            manager.DeleteReactions(context.GetCallerOdinIdOrFail(), file.FileId);

            await mediator.Publish(new AllReactionsByFileDeleted()
            {
                FileId = file
            });
        }
    }

    public async Task<GetReactionsResponse> GetReactions(InternalDriveFileId file, int cursor, int maxCount)
    {
        contextAccessor.GetCurrent().PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);

        var manager = await driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId);
        if (manager != null)
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