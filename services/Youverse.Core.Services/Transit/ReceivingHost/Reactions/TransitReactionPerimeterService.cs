using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.DataSubscription.Follower;
using Youverse.Core.Services.Drives.Reactions;
using Youverse.Core.Services.Transit.SendingHost;

namespace Youverse.Core.Services.Transit.ReceivingHost.Reactions;

/// <summary>
/// Handles incoming reactions and queries from followers
/// </summary>
public class TransitReactionPerimeterService : TransitServiceBase
{
    private readonly ReactionContentService _reactionContentService;

    public TransitReactionPerimeterService(ReactionContentService reactionContentService,
        IOdinHttpClientFactory odinHttpClientFactory,
        ICircleNetworkService circleNetworkService,
        FollowerService followerService,
        OdinContextAccessor contextAccessor,
        FileSystemResolver fileSystemResolver) :
        base(odinHttpClientFactory, circleNetworkService, contextAccessor, followerService, fileSystemResolver)
    {
        _reactionContentService = reactionContentService;
    }

    public async Task AddReaction(SharedSecretEncryptedTransitPayload payload)
    {
        var request = await DecryptUsingSharedSecret<AddRemoteReactionRequest>(payload, ClientAccessTokenSource.Circle);
        var fileId = await ResolveInternalFile(request.File);
        if (null == fileId)
        {
            throw new YouverseRemoteIdentityException("Invalid global transit id");
        }

        _reactionContentService.AddReaction(fileId.Value, request.Reaction);
    }

    public async Task DeleteReaction(SharedSecretEncryptedTransitPayload payload)
    {
        var request = await DecryptUsingSharedSecret<DeleteReactionRequestByGlobalTransitId>(payload, ClientAccessTokenSource.Circle);

        var fileId = await ResolveInternalFile(request.File);
        if (null == fileId)
        {
            throw new YouverseRemoteIdentityException("Invalid global transit id");
        }

        _reactionContentService.DeleteReaction(fileId.Value, request.Reaction);
    }

    public async Task<GetReactionCountsResponse> GetReactionCountsByFile(SharedSecretEncryptedTransitPayload payload)
    {
        var request = await DecryptUsingSharedSecret<GetRemoteReactionsRequest>(payload, ClientAccessTokenSource.Circle);

        var fileId = await ResolveInternalFile(request.File);
        if (null == fileId)
        {
            throw new YouverseRemoteIdentityException("Invalid global transit id");
        }

        return _reactionContentService.GetReactionCountsByFile(fileId.Value);
    }

    public async Task<List<string>> GetReactionsByIdentityAndFile(SharedSecretEncryptedTransitPayload payload)
    {
        var request = await DecryptUsingSharedSecret<TransitGetReactionsByIdentityRequest>(payload, ClientAccessTokenSource.Circle);

        var fileId = await ResolveInternalFile(request.File);
        if (null == fileId)
        {
            throw new YouverseRemoteIdentityException("Invalid global transit id");
        }

        return _reactionContentService.GetReactionsByIdentityAndFile(request.Identity, fileId.Value);
    }

    public async Task DeleteAllReactions(SharedSecretEncryptedTransitPayload payload)
    {
        var request = await DecryptUsingSharedSecret<DeleteReactionRequestByGlobalTransitId>(payload, ClientAccessTokenSource.Circle);

        var fileId = await ResolveInternalFile(request.File);
        if (null == fileId)
        {
            throw new YouverseRemoteIdentityException("Invalid global transit id");
        }

        _reactionContentService.DeleteAllReactions(fileId.Value);
    }

    public async Task<GetReactionsPerimeterResponse> GetReactions(SharedSecretEncryptedTransitPayload payload)
    {
        var request = await DecryptUsingSharedSecret<GetRemoteReactionsRequest>(payload, ClientAccessTokenSource.Circle);

        var fileId = await ResolveInternalFile(request.File);
        if (null == fileId)
        {
            throw new YouverseRemoteIdentityException("Invalid global transit id");
        }

        var list = _reactionContentService.GetReactions(fileId.Value, request.Cursor, request.MaxRecords);

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