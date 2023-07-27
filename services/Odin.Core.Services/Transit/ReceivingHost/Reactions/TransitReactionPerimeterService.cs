using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Contacts.Circle.Membership;
using Odin.Core.Services.DataSubscription.Follower;
using Odin.Core.Services.Drives.Reactions;
using Odin.Core.Services.Transit.SendingHost;

namespace Odin.Core.Services.Transit.ReceivingHost.Reactions;

/// <summary>
/// Handles incoming reactions and queries from followers
/// </summary>
public class TransitReactionPerimeterService : TransitServiceBase
{
    private readonly ReactionContentService _reactionContentService;

    public TransitReactionPerimeterService(ReactionContentService reactionContentService,
        IOdinHttpClientFactory odinHttpClientFactory,
        CircleNetworkService circleNetworkService,
        FollowerService followerService,
        OdinContextAccessor contextAccessor,
        FileSystemResolver fileSystemResolver) :
        base(odinHttpClientFactory, circleNetworkService, contextAccessor, followerService, fileSystemResolver)
    {
        _reactionContentService = reactionContentService;
    }

    public async Task AddReaction(SharedSecretEncryptedTransitPayload payload)
    {
        var request = await DecryptUsingSharedSecret<AddRemoteReactionRequest>(payload);
        var fileId = await ResolveInternalFile(request.File);
        if (null == fileId)
        {
            throw new OdinRemoteIdentityException("Invalid global transit id");
        }

        await _reactionContentService.AddReaction(fileId.Value, request.Reaction);
    }

    public async Task DeleteReaction(SharedSecretEncryptedTransitPayload payload)
    {
        var request = await DecryptUsingSharedSecret<DeleteReactionRequestByGlobalTransitId>(payload);

        var fileId = await ResolveInternalFile(request.File);
        if (null == fileId)
        {
            throw new OdinRemoteIdentityException("Invalid global transit id");
        }

        await _reactionContentService.DeleteReaction(fileId.Value, request.Reaction);
    }

    public async Task<GetReactionCountsResponse> GetReactionCountsByFile(SharedSecretEncryptedTransitPayload payload)
    {
        var request = await DecryptUsingSharedSecret<GetRemoteReactionsRequest>(payload);

        var fileId = await ResolveInternalFile(request.File);
        if (null == fileId)
        {
            throw new OdinRemoteIdentityException("Invalid global transit id");
        }

        return await _reactionContentService.GetReactionCountsByFile(fileId.Value);
    }

    public async Task<List<string>> GetReactionsByIdentityAndFile(SharedSecretEncryptedTransitPayload payload)
    {
        var request = await DecryptUsingSharedSecret<TransitGetReactionsByIdentityRequest>(payload);

        var fileId = await ResolveInternalFile(request.File);
        if (null == fileId)
        {
            throw new OdinRemoteIdentityException("Invalid global transit id");
        }

        return await _reactionContentService.GetReactionsByIdentityAndFile(request.Identity, fileId.Value);
    }

    public async Task DeleteAllReactions(SharedSecretEncryptedTransitPayload payload)
    {
        var request = await DecryptUsingSharedSecret<DeleteReactionRequestByGlobalTransitId>(payload);

        var fileId = await ResolveInternalFile(request.File);
        if (null == fileId)
        {
            throw new OdinRemoteIdentityException("Invalid global transit id");
        }

        await _reactionContentService.DeleteAllReactions(fileId.Value);
    }

    public async Task<GetReactionsPerimeterResponse> GetReactions(SharedSecretEncryptedTransitPayload payload)
    {
        var request = await DecryptUsingSharedSecret<GetRemoteReactionsRequest>(payload);

        var fileId = await ResolveInternalFile(request.File);
        if (null == fileId)
        {
            throw new OdinRemoteIdentityException("Invalid global transit id");
        }

        var list = await _reactionContentService.GetReactions(fileId.Value, request.Cursor, request.MaxRecords);

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