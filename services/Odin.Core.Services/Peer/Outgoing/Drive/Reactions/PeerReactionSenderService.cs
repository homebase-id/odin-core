using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives.Reactions;
using Odin.Core.Services.Membership.Connections;
using Odin.Core.Services.Peer.Encryption;
using Odin.Core.Services.Peer.Incoming.Reactions;
using Refit;

namespace Odin.Core.Services.Peer.Outgoing.Drive.Reactions;

/// <summary/>
public class PeerReactionSenderService(
    IOdinHttpClientFactory odinHttpClientFactory,
    CircleNetworkService circleNetworkService,
    OdinContextAccessor contextAccessor,
    FileSystemResolver fileSystemResolver)
    : PeerServiceBase(odinHttpClientFactory,
        circleNetworkService, contextAccessor, fileSystemResolver)
{
    private readonly OdinContextAccessor _contextAccessor = contextAccessor;

    /// <summary />
    public async Task AddReaction(OdinId odinId, AddRemoteReactionRequest request)
    {
        var (token, client) = await CreateReactionContentClient(odinId);

        SharedSecretEncryptedTransitPayload payload = this.CreateSharedSecretEncryptedPayload(token, request);

        var response = await client.AddReaction(payload);

        AssertValidResponse(response);
    }

    /// <summary />
    public async Task<GetReactionsPerimeterResponse> GetReactions(OdinId odinId, GetRemoteReactionsRequest request)
    {
        var (token, client) = await CreateReactionContentClient(odinId);
        SharedSecretEncryptedTransitPayload payload = this.CreateSharedSecretEncryptedPayload(token, request);
        var response = await client.GetReactions(payload);
        return response.Content;
    }

    /// <summary />
    public async Task<GetReactionCountsResponse> GetReactionCounts(OdinId odinId, GetRemoteReactionsRequest request)
    {
        var (token, client) = await CreateReactionContentClient(odinId);
        SharedSecretEncryptedTransitPayload payload = this.CreateSharedSecretEncryptedPayload(token, request);
        var response = await client.GetReactionCountsByFile(payload);
        return response.Content;
    }

    public async Task<List<string>> GetReactionsByIdentityAndFile(OdinId odinId, TransitGetReactionsByIdentityRequest request)
    {
        var (token, client) = await CreateReactionContentClient(odinId);
        SharedSecretEncryptedTransitPayload payload = this.CreateSharedSecretEncryptedPayload(token, request);
        var response = await client.GetReactionsByIdentity(payload);
        return response.Content;
    }

    public async Task DeleteReaction(OdinId odinId, DeleteReactionRequestByGlobalTransitId request)
    {
        var (token, client) = await CreateReactionContentClient(odinId);
        SharedSecretEncryptedTransitPayload payload = this.CreateSharedSecretEncryptedPayload(token, request);
        await client.DeleteReactionContent(payload);
    }

    public async Task DeleteAllReactions(OdinId odinId, DeleteReactionRequestByGlobalTransitId request)
    {
        var (token, client) = await CreateReactionContentClient(odinId);
        SharedSecretEncryptedTransitPayload payload = this.CreateSharedSecretEncryptedPayload(token, request);
        await client.GetReactionsByIdentity(payload);
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
        if (sharedSecretEncryptedFileHeader.FileMetadata.IsEncrypted)
        {
            var currentKey = icr.CreateClientAccessToken(_contextAccessor.GetCurrent().PermissionsContext.GetIcrKey()).SharedSecret;
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
            // throw new OdinClientException("Remote server returned 403", OdinClientErrorCode.RemoteServerReturnedForbidden);
            throw new OdinSecurityException("Remote server returned 403");
        }

        if (response.StatusCode == HttpStatusCode.InternalServerError)
        {
            throw new OdinClientException("Remote server returned 500", OdinClientErrorCode.RemoteServerReturnedInternalServerError);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new OdinSystemException($"Unhandled transit error response: {response.StatusCode}");
        }
    }
}