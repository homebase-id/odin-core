using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Util;
using Odin.Services.Apps;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.Reactions;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Incoming.Reactions;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Reactions;

public enum AddReactionResult
{
    Success = 200,
    AccessDenied = 403,
    RemoteServerError = 500
}

/// <summary/>
public class PeerReactionSenderService(
    IOdinHttpClientFactory odinHttpClientFactory,
    CircleNetworkService circleNetworkService,
    OdinContextAccessor contextAccessor,
    FileSystemResolver fileSystemResolver,
    ReactionContentService localReactionService,
    OdinConfiguration odinConfiguration)
    : PeerServiceBase(odinHttpClientFactory,
        circleNetworkService, contextAccessor, fileSystemResolver)
{
    private readonly OdinContextAccessor _contextAccessor = contextAccessor;


    public async Task<AddGroupReactionResponse> AddGroupReaction(IEnumerable<OdinId> recipients, AddRemoteReactionRequest request)
    {
        var internalFileId = await ResolveInternalFile(request.File);

        if (null == internalFileId)
        {
            throw new OdinClientException("No local file found for global transit id", OdinClientErrorCode.InvalidFile);
        }

        // add a local reaction
        await localReactionService.AddReaction(internalFileId.Value, request.Reaction);

        //broadcast to recipients
        var response = new AddGroupReactionResponse();
        var tasks = new List<Task<(OdinId recipient, ApiResponse<HttpContent> response)>>();
        var odinIds = recipients as OdinId[] ?? recipients.ToArray();
        tasks.AddRange(odinIds.Select(id => SendReactionInternal(id, request)));
        await Task.WhenAll(tasks);

        tasks.ForEach(task =>
        {
            var sendResponse = task.Result;
            response.Responses.Add(new RemoteAddDeleteReactionResponse()
            {
                Recipient = sendResponse.recipient,
                RemoteHttpStatusCode = sendResponse.response.StatusCode
            });
        });

        return response;
    }

    public async Task<DeleteGroupReactionResponse> DeleteGroupReaction(IEnumerable<OdinId> recipients, DeleteReactionRequestByGlobalTransitId request)
    {
        var internalFileId = await ResolveInternalFile(request.File);

        if (null == internalFileId)
        {
            throw new OdinClientException("No local file found for global transit id", OdinClientErrorCode.InvalidFile);
        }

        // add a local reaction
        await localReactionService.DeleteReaction(internalFileId.Value, request.Reaction);

        //broadcast to recipients
        var response = new DeleteGroupReactionResponse();
        var tasks = new List<Task<(OdinId recipient, ApiResponse<HttpContent> response)>>();
        var odinIds = recipients as OdinId[] ?? recipients.ToArray();
        tasks.AddRange(odinIds.Select(id => DeleteReactionInternal(id, request)));
        await Task.WhenAll(tasks);

        tasks.ForEach(task =>
        {
            var sendResponse = task.Result;
            response.Responses.Add(new RemoteAddDeleteReactionResponse()
            {
                Recipient = sendResponse.recipient,
                RemoteHttpStatusCode = sendResponse.response.StatusCode
            });
        });

        return response;
    }

    /// <summary />
    public async Task AddReaction(OdinId odinId, AddRemoteReactionRequest request)
    {
        var result = await SendReactionInternal(odinId, request);
        AssertValidResponse(result.response);
    }

    /// <summary />
    public async Task<GetReactionsPerimeterResponse> GetReactions(OdinId odinId, GetRemoteReactionsRequest request)
    {
        var (token, client) = await CreateReactionContentClient(odinId);
        SharedSecretEncryptedTransitPayload payload = CreateSharedSecretEncryptedPayload(token, request);

        try
        {
            ApiResponse<GetReactionsPerimeterResponse> response = null;
            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.PeerOperationMaxAttempts,
                odinConfiguration.Host.PeerOperationDelayMs,
                CancellationToken.None,
                async () => { response = await client.GetReactions(payload); });

            return response.Content;
        }
        catch (TryRetryException ex)
        {
            HandleTryRetryException(ex);
            throw;
        }
    }

    /// <summary />
    public async Task<GetReactionCountsResponse> GetReactionCounts(OdinId odinId, GetRemoteReactionsRequest request)
    {
        var (token, client) = await CreateReactionContentClient(odinId);
        SharedSecretEncryptedTransitPayload payload = this.CreateSharedSecretEncryptedPayload(token, request);

        try
        {
            ApiResponse<GetReactionCountsResponse> response = null;
            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.PeerOperationMaxAttempts,
                odinConfiguration.Host.PeerOperationDelayMs,
                CancellationToken.None,
                async () => { response = await client.GetReactionCountsByFile(payload); });

            return response.Content;
        }
        catch (TryRetryException ex)
        {
            HandleTryRetryException(ex);
            throw;
        }
    }

    public async Task<List<string>> GetReactionsByIdentityAndFile(OdinId odinId, PeerGetReactionsByIdentityRequest request)
    {
        var (token, client) = await CreateReactionContentClient(odinId);
        SharedSecretEncryptedTransitPayload payload = this.CreateSharedSecretEncryptedPayload(token, request);

        try
        {
            ApiResponse<List<string>> response = null;
            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.PeerOperationMaxAttempts,
                odinConfiguration.Host.PeerOperationDelayMs,
                CancellationToken.None,
                async () => { response = await client.GetReactionsByIdentity(payload); });

            return response.Content;
        }
        catch (TryRetryException ex)
        {
            HandleTryRetryException(ex);
            throw;
        }
    }

    public async Task DeleteReaction(OdinId odinId, DeleteReactionRequestByGlobalTransitId request)
    {
        var response = await DeleteReactionInternal(odinId, request);
    }

    public async Task DeleteAllReactions(OdinId odinId, DeleteReactionRequestByGlobalTransitId request)
    {
        var (token, client) = await CreateReactionContentClient(odinId);
        SharedSecretEncryptedTransitPayload payload = this.CreateSharedSecretEncryptedPayload(token, request);

        try
        {
            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.PeerOperationMaxAttempts,
                odinConfiguration.Host.PeerOperationDelayMs,
                CancellationToken.None,
                async () => { await client.GetReactionsByIdentity(payload); });
        }
        catch (TryRetryException ex)
        {
            HandleTryRetryException(ex);
            throw;
        }
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

    private void HandleTryRetryException(TryRetryException ex)
    {
        var e = ex.InnerException;
        if (e is TaskCanceledException || e is HttpRequestException || e is OperationCanceledException)
        {
            throw new OdinClientException("Failed while calling remote identity", e);
        }
    }

    private async Task<(OdinId recipient, ApiResponse<HttpContent> response)> SendReactionInternal(OdinId odinId, AddRemoteReactionRequest request)
    {
        var (token, client) = await CreateReactionContentClient(odinId);

        SharedSecretEncryptedTransitPayload payload = this.CreateSharedSecretEncryptedPayload(token, request);
        ApiResponse<HttpContent> response = null;
        try
        {
            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.PeerOperationMaxAttempts,
                odinConfiguration.Host.PeerOperationDelayMs,
                CancellationToken.None,
                async () => { response = await client.AddReaction(payload); });
        }
        catch (TryRetryException ex)
        {
            HandleTryRetryException(ex);
            throw;
        }

        return (odinId, response);
    }

    private async Task<(OdinId recipient, ApiResponse<HttpContent> response)> DeleteReactionInternal(OdinId odinId,
        DeleteReactionRequestByGlobalTransitId request)
    {
        var (token, client) = await CreateReactionContentClient(odinId);
        SharedSecretEncryptedTransitPayload payload = this.CreateSharedSecretEncryptedPayload(token, request);
        
        ApiResponse<HttpContent> response = null;
        try
        {
            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.PeerOperationMaxAttempts,
                odinConfiguration.Host.PeerOperationDelayMs,
                CancellationToken.None,
                async () => { response = await client.DeleteReactionContent(payload); });
        }
        catch (TryRetryException ex)
        {
            HandleTryRetryException(ex);
            throw;
        }

        return (odinId, response);
    }
}