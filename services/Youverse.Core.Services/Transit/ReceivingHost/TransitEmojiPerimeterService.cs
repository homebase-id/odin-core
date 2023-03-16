using System;
using System.Threading.Tasks;
using Youverse.Core.Exceptions;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.DataSubscription.Follower;
using Youverse.Core.Services.Drives.Reactions;
using Youverse.Core.Services.Transit.SendingHost;

namespace Youverse.Core.Services.Transit.ReceivingHost;

/// <summary>
/// Handles incoming emoji reactions and queries from followers
/// </summary>
public class TransitEmojiPerimeterService : TransitServiceBase
{
    private readonly EmojiReactionService _emojiReactionService;

    public TransitEmojiPerimeterService(EmojiReactionService emojiReactionService,
        IDotYouHttpClientFactory dotYouHttpClientFactory,
        ICircleNetworkService circleNetworkService,
        FollowerService followerService,
        DotYouContextAccessor contextAccessor,
        FileSystemResolver fileSystemResolver) :
        base(dotYouHttpClientFactory, circleNetworkService, contextAccessor, followerService, fileSystemResolver)
    {
        _emojiReactionService = emojiReactionService;
    }

    public async Task AddReaction(SharedSecretEncryptedTransitPayload payload)
    {
        var request = await base.DecryptUsingSharedSecret<AddRemoteReactionRequest>(payload, ClientAccessTokenSource.Circle);
        var fileId = await base.ResolveInternalFile(request.File);
        if (null == fileId)
        {
            throw new YouverseRemoteIdentityException("Invalid global transit id");
        }

        _emojiReactionService.AddReaction(fileId.Value, request.Reaction);
    }

    public Task<GetReactionsResponse> DeleteReaction(SharedSecretEncryptedTransitPayload payload)
    {
        throw new NotImplementedException();
    }

    public Task<GetReactionCountsResponse> GetReactionCountsByFile(SharedSecretEncryptedTransitPayload payload)
    {
        throw new NotImplementedException();
    }

    public Task<GetReactionsResponse> GetReactionsByIdentityAndFile(SharedSecretEncryptedTransitPayload payload)
    {
        throw new NotImplementedException();
    }

    public Task<GetReactionsResponse> DeleteReactions(SharedSecretEncryptedTransitPayload payload)
    {
        throw new NotImplementedException();
    }

    public async Task<GetReactionsResponse> GetReactions(SharedSecretEncryptedTransitPayload payload)
    {
        var request = await base.DecryptUsingSharedSecret<GetRemoteReactionsRequest>(payload, ClientAccessTokenSource.Circle);

        var fileId = await base.ResolveInternalFile(request.File);
        if (null == fileId)
        {
            throw new YouverseRemoteIdentityException("Invalid global transit id");
        }

        return _emojiReactionService.GetReactions(fileId.Value, request.Cursor, request.MaxRecords);
    }
}