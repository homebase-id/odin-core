using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Services.Base;
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
    FileSystemResolver fileSystemResolver)
    : PeerServiceBase(odinHttpClientFactory, circleNetworkService, fileSystemResolver)
{
    public async Task AddReaction(SharedSecretEncryptedTransitPayload payload, IOdinContext odinContext, IdentityDatabase db)
    {
        var request = await DecryptUsingSharedSecretAsync<AddRemoteReactionRequest>(payload, odinContext);
        var fileId = await ResolveInternalFile(request.File, odinContext, db, failIfNull: true);
        await reactionContentService.AddReactionAsync(fileId!.Value, request.Reaction, odinContext.GetCallerOdinIdOrFail(), odinContext, db);
    }

    public async Task DeleteReaction(SharedSecretEncryptedTransitPayload payload, IOdinContext odinContext, IdentityDatabase db)
    {
        var request = await DecryptUsingSharedSecretAsync<DeleteReactionRequestByGlobalTransitId>(payload, odinContext);

        var fileId = await ResolveInternalFile(request.File, odinContext, db, failIfNull: true);
        await reactionContentService.DeleteReactionAsync(fileId!.Value, request.Reaction, odinContext.GetCallerOdinIdOrFail(), odinContext, db);
    }

    public async Task<GetReactionCountsResponse> GetReactionCountsByFile(SharedSecretEncryptedTransitPayload payload, IOdinContext odinContext,
        IdentityDatabase db)
    {
        var request = await DecryptUsingSharedSecretAsync<GetRemoteReactionsRequest>(payload, odinContext);

        var fileId = await ResolveInternalFile(request.File, odinContext, db, failIfNull: true);
        return await reactionContentService.GetReactionCountsByFileAsync(fileId!.Value, odinContext, db);
    }

    public async Task<List<string>> GetReactionsByIdentityAndFile(SharedSecretEncryptedTransitPayload payload, IOdinContext odinContext, IdentityDatabase db)
    {
        var request = await DecryptUsingSharedSecretAsync<PeerGetReactionsByIdentityRequest>(payload, odinContext);

        var fileId = await ResolveInternalFile(request.File, odinContext, db, failIfNull: true);
        return await reactionContentService.GetReactionsByIdentityAndFileAsync(request.Identity, fileId!.Value, odinContext, db);
    }

    public async Task DeleteAllReactions(SharedSecretEncryptedTransitPayload payload, IOdinContext odinContext, IdentityDatabase db)
    {
        var request = await DecryptUsingSharedSecretAsync<DeleteReactionRequestByGlobalTransitId>(payload, odinContext);

        var fileId = await ResolveInternalFile(request.File, odinContext, db, failIfNull: true);
        await reactionContentService.DeleteAllReactionsAsync(fileId!.Value, odinContext, db);
    }

    public async Task<GetReactionsPerimeterResponse> GetReactions(SharedSecretEncryptedTransitPayload payload, IOdinContext odinContext, IdentityDatabase db)
    {
        var request = await DecryptUsingSharedSecretAsync<GetRemoteReactionsRequest>(payload, odinContext);

        var fileId = await ResolveInternalFile(request.File, odinContext, db, failIfNull: true);
        
        var list = await reactionContentService.GetReactionsAsync(fileId!.Value, request.Cursor, request.MaxRecords, odinContext, db);

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