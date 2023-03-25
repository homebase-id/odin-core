using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Exceptions;
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

    public async Task DeleteReaction(SharedSecretEncryptedTransitPayload payload)
    {
        var request = await base.DecryptUsingSharedSecret<DeleteReactionRequestByGlobalTransitId>(payload, ClientAccessTokenSource.Circle);

        var fileId = await base.ResolveInternalFile(request.File);
        if (null == fileId)
        {
            throw new YouverseRemoteIdentityException("Invalid global transit id");
        }

        _emojiReactionService.DeleteReaction(fileId.Value, request.Reaction);
    }

    public async Task<GetReactionCountsResponse> GetReactionCountsByFile(SharedSecretEncryptedTransitPayload payload)
    {
        var request = await base.DecryptUsingSharedSecret<GetRemoteReactionsRequest>(payload, ClientAccessTokenSource.Circle);

        var fileId = await base.ResolveInternalFile(request.File);
        if (null == fileId)
        {
            throw new YouverseRemoteIdentityException("Invalid global transit id");
        }

        return _emojiReactionService.GetReactionCountsByFile(fileId.Value);
    }

    public async Task<List<string>> GetReactionsByIdentityAndFile(SharedSecretEncryptedTransitPayload payload)
    {
        var request = await base.DecryptUsingSharedSecret<TransitGetReactionsByIdentityRequest>(payload, ClientAccessTokenSource.Circle);

        var fileId = await base.ResolveInternalFile(request.File);
        if (null == fileId)
        {
            throw new YouverseRemoteIdentityException("Invalid global transit id");
        }

        return _emojiReactionService.GetReactionsByIdentityAndFile(request.Identity, fileId.Value);
    }

    public async Task DeleteAllReactions(SharedSecretEncryptedTransitPayload payload)
    {
        var request = await base.DecryptUsingSharedSecret<DeleteReactionRequestByGlobalTransitId>(payload, ClientAccessTokenSource.Circle);

        var fileId = await base.ResolveInternalFile(request.File);
        if (null == fileId)
        {
            throw new YouverseRemoteIdentityException("Invalid global transit id");
        }

        _emojiReactionService.DeleteAllReactions(fileId.Value);
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