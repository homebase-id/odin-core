using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.DataSubscription.Follower;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.Reactions;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Transit.SendingHost;

public class AddRemoteReactionRequest
{
    public GlobalTransitIdFileIdentifier File { get; set; }
    public string Reaction { get; set; }
}

public class GetRemoteReactionsRequest
{
    public GlobalTransitIdFileIdentifier File { get; set; }
    public int Cursor { get; set; }
    public int MaxRecords { get; set; }
}

public class TransitAddReactionRequest
{
    public string OdinId { get; set; }

    public AddRemoteReactionRequest Request { get; set; }
}

public class TransitGetReactionsRequest
{
    public string OdinId { get; set; }

    public GetRemoteReactionsRequest Request { get; set; }
}

public class TransitDeleteReactionRequest
{
    public string OdinId { get; set; }

    public GlobalTransitIdFileIdentifier File { get; set; }
}

public class TransitGetReactionsByIdentityRequest
{
    /// <summary>
    /// The remote identity server 
    /// </summary>
    public OdinId OdinId { get; set; }

    public OdinId Identity { get; set; }
        
    public GlobalTransitIdFileIdentifier File { get; set; }
}

/// <summary/>
public class TransitEmojiSenderService : TransitServiceBase
{
    private readonly DotYouContextAccessor _contextAccessor;

    public TransitEmojiSenderService(IDotYouHttpClientFactory dotYouHttpClientFactory,
        ICircleNetworkService circleNetworkService,
        DotYouContextAccessor contextAccessor, FollowerService followerService, FileSystemResolver fileSystemResolver) : base(dotYouHttpClientFactory,
        circleNetworkService, contextAccessor,
        followerService, fileSystemResolver)
    {
        _contextAccessor = contextAccessor;
    }

    /// <summary />
    public async Task AddReaction(OdinId odinId, AddRemoteReactionRequest request)
    {
        var (token, client) = await CreateClient(odinId, ClientAccessTokenSource.DataSubscription);

        SharedSecretEncryptedTransitPayload payload = this.CreateSharedSecretEncryptedPayload(token, request);

        var apiResponse = await client.AddReaction(payload);
        if (apiResponse.IsSuccessStatusCode)
        {
            return;
        }

        throw new YouverseRemoteIdentityException("Remote server returned unsuccessful status code");
    }

    /// <summary />
    public async Task<GetReactionsResponse> GetReactions(OdinId odinId, GetRemoteReactionsRequest request)
    {
        var (token, client) = await CreateClient(odinId, ClientAccessTokenSource.DataSubscription);
        SharedSecretEncryptedTransitPayload payload = this.CreateSharedSecretEncryptedPayload(token, request);
        var response = await client.GetReactions(payload);
        return response.Content;
    }

    /// <summary />
    public Task<GetReactionCountsResponse> GetReactionCounts(OdinId odinId, GetRemoteReactionsRequest request)
    {
        // var (_, client) = await CreateClient(odinId, ClientAccessTokenSource.DataSubscription);
        // var response = await client.GetReactionCountsByFile(request.File);
        // return response.Content;
        return null;
    }

    public Task<List<string>> GetReactionsByIdentityAndFile(OdinId odinId, TransitGetReactionsByIdentityRequest request)
    {
        // var (_, client) = await CreateClient(odinId, ClientAccessTokenSource.DataSubscription);
        // var response = await client.GetReactionsByIdentityAndFile(request.Identity, request.File);
        // return response.Content;
        return null;
    }


    /// <summary>
    /// Converts the icr-shared-secret-encrypted key header to an owner-shared-secret encrypted key header
    /// </summary>
    /// <param name="sharedSecretEncryptedFileHeader"></param>
    /// <param name="icr"></param>
    private SharedSecretEncryptedFileHeader TransformSharedSecret(SharedSecretEncryptedFileHeader sharedSecretEncryptedFileHeader,
        IdentityConnectionRegistration icr)
    {
        EncryptedKeyHeader ownerSharedSecretEncryptedKeyHeader;
        if (sharedSecretEncryptedFileHeader.FileMetadata.PayloadIsEncrypted)
        {
            var currentKey = icr.ClientAccessTokenSharedSecret.ToSensitiveByteArray();
            var icrEncryptedKeyHeader = sharedSecretEncryptedFileHeader.SharedSecretEncryptedKeyHeader;
            ownerSharedSecretEncryptedKeyHeader = ReEncrypt(currentKey, icrEncryptedKeyHeader);
        }
        else
        {
            ownerSharedSecretEncryptedKeyHeader = EncryptedKeyHeader.Empty();
        }

        sharedSecretEncryptedFileHeader.SharedSecretEncryptedKeyHeader = ownerSharedSecretEncryptedKeyHeader;

        return sharedSecretEncryptedFileHeader;
    }

    private EncryptedKeyHeader ReEncrypt(SensitiveByteArray currentKey, EncryptedKeyHeader encryptedKeyHeader)
    {
        var newKey = _contextAccessor.GetCurrent().PermissionsContext.SharedSecretKey;
        var keyHeader = encryptedKeyHeader.DecryptAesToKeyHeader(ref currentKey);
        var newEncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, keyHeader.Iv, ref newKey);
        keyHeader.AesKey.Wipe();

        return newEncryptedKeyHeader;
    }

    private void AssertValidResponse<T>(ApiResponse<T> response)
    {
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new YouverseClientException("Remote server returned 403", YouverseClientErrorCode.RemoteServerReturnedForbidden);
        }

        if (response.StatusCode == HttpStatusCode.InternalServerError)
        {
            throw new YouverseClientException("Remote server returned 500", YouverseClientErrorCode.RemoteServerReturnedInternalServerError);
        }

        if (!response.IsSuccessStatusCode || response.Content == null)
        {
            throw new YouverseSystemException($"Unhandled transit error response: {response.StatusCode}");
        }
    }

    public void DeleteReaction(TransitDeleteReactionRequest request)
    {
        throw new System.NotImplementedException();
    }

    public void DeleteAllReactions(TransitDeleteReactionRequest request)
    {
        throw new System.NotImplementedException();
    }
}