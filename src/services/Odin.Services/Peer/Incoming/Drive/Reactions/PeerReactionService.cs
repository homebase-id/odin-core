using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Drives.Reactions;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Incoming.Drive.Reactions.Inbox;
using Odin.Services.Peer.Incoming.Reactions;
using Odin.Services.Peer.Outgoing.Drive.Reactions;

namespace Odin.Services.Peer.Incoming.Drive.Reactions;

/// <summary>
/// Handles incoming reactions and queries from followers
/// </summary>
public class PeerReactionService(
    ReactionContentService reactionContentService,
    IOdinHttpClientFactory odinHttpClientFactory,
    CircleNetworkService circleNetworkService,
    OdinContextAccessor contextAccessor,
    FileSystemResolver fileSystemResolver,
    DriveManager driveManager,
    PeerReactionInbox peerReactionInbox)
    : PeerServiceBase(odinHttpClientFactory, circleNetworkService, contextAccessor, fileSystemResolver)
{
    private readonly OdinContextAccessor _contextAccessor = contextAccessor;

    public async Task ProcessInbox(GlobalTransitIdFileIdentifier file)
    {
        // Now that the file is stored, unpack any queued reactions
        var reactions = await peerReactionInbox.GetItems(file.GlobalTransitId);
        foreach (var r in reactions)
        {
            var wasEnqueued = await this.AddReaction(r.Payload);
        }
    }

    public async Task<bool> AddReaction(SharedSecretEncryptedTransitPayload payload)
    {
        var request = await DecryptUsingSharedSecret<AddRemoteReactionRequest>(payload);
        var driveId = await driveManager.GetDriveIdByAlias(request.File.TargetDrive);

        using (new PeerReactionSecurityContext(_contextAccessor, driveId.GetValueOrDefault(), request.File.TargetDrive))
        {
            var fileId = await ResolveInternalFile(request.File);

            if (null == fileId)
            {
                //Enqueue these so we can replay them later
                await peerReactionInbox.EnqueueAddReaction(request.File, payload, request);
                return true;
            }

            await reactionContentService.AddReaction(fileId.Value, request.Reaction);
            return false;
        }
    }

    public async Task DeleteReaction(SharedSecretEncryptedTransitPayload payload)
    {
        var request = await DecryptUsingSharedSecret<DeleteReactionRequestByGlobalTransitId>(payload);

        InternalDriveFileId? fileId;
        var driveId = await driveManager.GetDriveIdByAlias(request.File.TargetDrive);

        using (new PeerReactionSecurityContext(_contextAccessor, driveId.GetValueOrDefault(), request.File.TargetDrive))
        {
            fileId = await ResolveInternalFile(request.File);

            if (null == fileId)
            {
                //Enqueue these so we can replay them later
                await peerReactionInbox.EnqueueDeleteReaction(request.File, payload, request);
                return;
            }

            await reactionContentService.DeleteReaction(fileId.Value, request.Reaction);
        }
    }

    public async Task<GetReactionCountsResponse> GetReactionCountsByFile(SharedSecretEncryptedTransitPayload payload)
    {
        var request = await DecryptUsingSharedSecret<GetRemoteReactionsRequest>(payload);

        var fileId = await ResolveInternalFile(request.File);
        if (null == fileId)
        {
            throw new OdinRemoteIdentityException("Invalid global transit id");
        }

        return await reactionContentService.GetReactionCountsByFile(fileId.Value);
    }

    public async Task<List<string>> GetReactionsByIdentityAndFile(SharedSecretEncryptedTransitPayload payload)
    {
        var request = await DecryptUsingSharedSecret<PeerGetReactionsByIdentityRequest>(payload);

        var fileId = await ResolveInternalFile(request.File);
        if (null == fileId)
        {
            throw new OdinRemoteIdentityException("Invalid global transit id");
        }

        return await reactionContentService.GetReactionsByIdentityAndFile(request.Identity, fileId.Value);
    }

    /// <summary>
    /// Deletes all reactions for a given file
    /// </summary>
    public async Task DeleteAllReactions(SharedSecretEncryptedTransitPayload payload)
    {
        var request = await DecryptUsingSharedSecret<DeleteReactionRequestByGlobalTransitId>(payload);
        await this.DeleteAllReactions(request);
    }

    public async Task DeleteAllReactions(DeleteReactionRequestByGlobalTransitId request)
    {
        var fileId = await ResolveInternalFile(request.File);
        if (null == fileId)
        {
            return;
        }

        await reactionContentService.DeleteAllReactions(fileId.Value);
    }

    public async Task<GetReactionsPerimeterResponse> GetReactions(SharedSecretEncryptedTransitPayload payload)
    {
        var request = await DecryptUsingSharedSecret<GetRemoteReactionsRequest>(payload);

        var fileId = await ResolveInternalFile(request.File);
        if (null == fileId)
        {
            throw new OdinRemoteIdentityException("Invalid global transit id");
        }

        var list = await reactionContentService.GetReactions(fileId.Value, request.Cursor, request.MaxRecords);

        return new GetReactionsPerimeterResponse()
        {
            Reactions = list.Reactions.Select(r =>
                new PerimeterReaction()
                {
                    OdinId = r.OdinId,
                    ReactionContent = r.ReactionContent,
                    Created = r.Created,
                    GlobalTransitIdFileIdentifier = request.File
                }).ToList(),
            Cursor = list.Cursor
        };
    }
}