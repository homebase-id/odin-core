using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives.Reactions;
using Odin.Core.Services.Membership.Connections;
using Odin.Core.Services.Peer.Incoming.Reactions;
using Odin.Core.Services.Peer.Outgoing.Drive.Reactions;

namespace Odin.Core.Services.Peer.Incoming.Drive.Reactions;

/// <summary>
/// Handles incoming reactions and queries from followers
/// </summary>
public class PeerReactionService(
    ReactionContentService reactionContentService,
    IOdinHttpClientFactory odinHttpClientFactory,
    CircleNetworkService circleNetworkService,
    OdinContextAccessor contextAccessor,
    FileSystemResolver fileSystemResolver)
    : PeerServiceBase(odinHttpClientFactory, circleNetworkService, contextAccessor, fileSystemResolver)
{
    public async Task AddReaction(SharedSecretEncryptedTransitPayload payload)
    {
        var request = await DecryptUsingSharedSecret<AddRemoteReactionRequest>(payload);
        var fileId = await ResolveInternalFile(request.File);
        if (null == fileId)
        {
            throw new OdinRemoteIdentityException("Invalid global transit id");
        }

        await reactionContentService.AddReaction(fileId.Value, request.Reaction);
    }

    public async Task DeleteReaction(SharedSecretEncryptedTransitPayload payload)
    {
        var request = await DecryptUsingSharedSecret<DeleteReactionRequestByGlobalTransitId>(payload);

        var fileId = await ResolveInternalFile(request.File);
        if (null == fileId)
        {
            throw new OdinRemoteIdentityException("Invalid global transit id");
        }

        await reactionContentService.DeleteReaction(fileId.Value, request.Reaction);
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
        var request = await DecryptUsingSharedSecret<TransitGetReactionsByIdentityRequest>(payload);

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