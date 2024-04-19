using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Services.Base;
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
    FileSystemResolver fileSystemResolver)
    : PeerServiceBase(odinHttpClientFactory, circleNetworkService, fileSystemResolver)
{
    public async Task AddReaction(SharedSecretEncryptedTransitPayload payload, IOdinContext odinContext)
    {
        var request = await DecryptUsingSharedSecret<AddRemoteReactionRequest>(payload, odinContext);
        var fileId = await ResolveInternalFile(request.File,odinContext);
        if (null == fileId)
        {
            throw new OdinRemoteIdentityException("Invalid global transit id");
        }

        await reactionContentService.AddReaction(fileId.Value, request.Reaction, odinContext);
    }

    public async Task DeleteReaction(SharedSecretEncryptedTransitPayload payload, IOdinContext odinContext)
    {
        var request = await DecryptUsingSharedSecret<DeleteReactionRequestByGlobalTransitId>(payload, odinContext);

        var fileId = await ResolveInternalFile(request.File,odinContext);
        if (null == fileId)
        {
            throw new OdinRemoteIdentityException("Invalid global transit id");
        }

        await reactionContentService.DeleteReaction(fileId.Value, request.Reaction, odinContext);
    }

    public async Task<GetReactionCountsResponse> GetReactionCountsByFile(SharedSecretEncryptedTransitPayload payload, IOdinContext odinContext)
    {
        var request = await DecryptUsingSharedSecret<GetRemoteReactionsRequest>(payload, odinContext);

        var fileId = await ResolveInternalFile(request.File,odinContext);
        if (null == fileId)
        {
            throw new OdinRemoteIdentityException("Invalid global transit id");
        }

        return await reactionContentService.GetReactionCountsByFile(fileId.Value, odinContext);
    }

    public async Task<List<string>> GetReactionsByIdentityAndFile(SharedSecretEncryptedTransitPayload payload, IOdinContext odinContext)
    {
        var request = await DecryptUsingSharedSecret<PeerGetReactionsByIdentityRequest>(payload, odinContext);

        var fileId = await ResolveInternalFile(request.File,odinContext);
        if (null == fileId)
        {
            throw new OdinRemoteIdentityException("Invalid global transit id");
        }

        return await reactionContentService.GetReactionsByIdentityAndFile(request.Identity, fileId.Value, odinContext);
    }

    public async Task DeleteAllReactions(SharedSecretEncryptedTransitPayload payload, IOdinContext odinContext)
    {
        var request = await DecryptUsingSharedSecret<DeleteReactionRequestByGlobalTransitId>(payload, odinContext);

        var fileId = await ResolveInternalFile(request.File,odinContext);
        if (null == fileId)
        {
            throw new OdinRemoteIdentityException("Invalid global transit id");
        }

        await reactionContentService.DeleteAllReactions(fileId.Value, odinContext);
    }

    public async Task<GetReactionsPerimeterResponse> GetReactions(SharedSecretEncryptedTransitPayload payload, IOdinContext odinContext)
    {
        var request = await DecryptUsingSharedSecret<GetRemoteReactionsRequest>(payload, odinContext);

        var fileId = await ResolveInternalFile(request.File,odinContext);
        if (null == fileId)
        {
            throw new OdinRemoteIdentityException("Invalid global transit id");
        }

        var list = await reactionContentService.GetReactions(fileId.Value, request.Cursor, request.MaxRecords, odinContext);

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