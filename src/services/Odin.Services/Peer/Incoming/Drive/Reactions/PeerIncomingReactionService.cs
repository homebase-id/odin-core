using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives.Reactions;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Incoming.Reactions;
using Odin.Services.Peer.Outgoing.Drive.Reactions;

namespace Odin.Services.Peer.Incoming.Drive.Reactions;

/// <summary>
/// Handles incoming reactions and queries from followers
/// </summary>
public class PeerIncomingReactionService(
    ReactionContentService reactionContentService,
    IOdinHttpClientFactory odinHttpClientFactory,
    CircleNetworkService circleNetworkService,
    OdinConfiguration odinConfiguration,
    FileSystemResolver fileSystemResolver)
    : PeerServiceBase(odinHttpClientFactory, circleNetworkService, fileSystemResolver, odinConfiguration)
{
    public async Task AddReaction(SharedSecretEncryptedTransitPayload payload, IOdinContext odinContext)
    {
        var request = await DecryptUsingSharedSecretAsync<AddRemoteReactionRequest>(payload, odinContext);
        var fileId = await ResolveInternalFile(request.File, odinContext, failIfNull: true);
        await reactionContentService.AddReactionAsync(fileId!.Value, request.Reaction, odinContext.GetCallerOdinIdOrFail(), odinContext);
    }

    public async Task DeleteReaction(SharedSecretEncryptedTransitPayload payload, IOdinContext odinContext)
    {
        var request = await DecryptUsingSharedSecretAsync<DeleteReactionRequestByGlobalTransitId>(payload, odinContext);

        var fileId = await ResolveInternalFile(request.File, odinContext, failIfNull: true);
        await reactionContentService.DeleteReactionAsync(fileId!.Value, request.Reaction, odinContext.GetCallerOdinIdOrFail(), odinContext);
    }

    public async Task<GetReactionCountsResponse> GetReactionCountsByFile(SharedSecretEncryptedTransitPayload payload, IOdinContext odinContext)
    {
        var request = await DecryptUsingSharedSecretAsync<GetRemoteReactionsRequest>(payload, odinContext);

        var fileId = await ResolveInternalFile(request.File, odinContext, failIfNull: true);
        return await reactionContentService.GetReactionCountsByFileAsync(fileId!.Value, odinContext);
    }

    public async Task<List<string>> GetReactionsByIdentityAndFile(SharedSecretEncryptedTransitPayload payload, IOdinContext odinContext)
    {
        var request = await DecryptUsingSharedSecretAsync<PeerGetReactionsByIdentityRequest>(payload, odinContext);

        var fileId = await ResolveInternalFile(request.File, odinContext, failIfNull: true);
        return await reactionContentService.GetReactionsByIdentityAndFileAsync(request.Identity, fileId!.Value, odinContext);
    }

    public async Task DeleteAllReactions(SharedSecretEncryptedTransitPayload payload, IOdinContext odinContext)
    {
        var request = await DecryptUsingSharedSecretAsync<DeleteReactionRequestByGlobalTransitId>(payload, odinContext);

        var fileId = await ResolveInternalFile(request.File, odinContext, failIfNull: true);
        await reactionContentService.DeleteAllReactionsAsync(fileId!.Value, odinContext);
    }

    public async Task<GetReactionsPerimeterResponse> GetReactions(SharedSecretEncryptedTransitPayload payload, IOdinContext odinContext)
    {
        var request = await DecryptUsingSharedSecretAsync<GetRemoteReactionsRequest>(payload, odinContext);

        var fileId = await ResolveInternalFile(request.File, odinContext, failIfNull: true);
        
        int.TryParse(request.Cursor, out var c);
        var list = await reactionContentService.GetReactionsAsync(fileId!.Value, c, request.MaxRecords, odinContext);

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