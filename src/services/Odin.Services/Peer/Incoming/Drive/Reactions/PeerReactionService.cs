using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Odin.Core.Exceptions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Drives.Reactions;
using Odin.Services.Membership.Connections;
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
    DriveManager driveManager)
    : PeerServiceBase(odinHttpClientFactory, circleNetworkService, contextAccessor, fileSystemResolver)
{
    private readonly OdinContextAccessor _contextAccessor = contextAccessor;

    public async Task AddReaction(SharedSecretEncryptedTransitPayload payload)
    {
        var request = await DecryptUsingSharedSecret<AddRemoteReactionRequest>(payload);

        InternalDriveFileId? fileId;
        var driveId = await driveManager.GetDriveIdByAlias(request.File.TargetDrive);

        // Upgrade to owner access to let us reach the file by global transit id
        using (new PeerReactionSecurityContext(_contextAccessor, driveId.GetValueOrDefault(), request.File.TargetDrive))
        {
            fileId = await ResolveInternalFile(request.File);

            //TODO: here we need to enqueue the global transit id when
            // the reaction is for a file *might be* in the inbox

            if (null == fileId)
            {
                throw new OdinRemoteIdentityException("Invalid global transit id");
            }

            await reactionContentService.AddReaction(fileId.Value, request.Reaction);
        }
    }

    public async Task DeleteReaction(SharedSecretEncryptedTransitPayload payload)
    {
        var request = await DecryptUsingSharedSecret<DeleteReactionRequestByGlobalTransitId>(payload);

        // Upgrade to owner access to let us reach the file by global transit id
        InternalDriveFileId? fileId;
        var driveId = await driveManager.GetDriveIdByAlias(request.File.TargetDrive);

        // Upgrade to owner access to let us reach the file by global transit id
        using (new PeerReactionSecurityContext(_contextAccessor, driveId.GetValueOrDefault(), request.File.TargetDrive))
        {
            fileId = await ResolveInternalFile(request.File);

            if (null == fileId)
            {
                throw new OdinRemoteIdentityException("Invalid global transit id");
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

    public async Task DeleteAllReactions(SharedSecretEncryptedTransitPayload payload)
    {
        var request = await DecryptUsingSharedSecret<DeleteReactionRequestByGlobalTransitId>(payload);

        var fileId = await ResolveInternalFile(request.File);
        if (null == fileId)
        {
            throw new OdinRemoteIdentityException("Invalid global transit id");
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