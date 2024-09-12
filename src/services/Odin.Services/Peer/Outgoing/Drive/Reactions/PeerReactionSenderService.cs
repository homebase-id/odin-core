using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage.SQLite;
using Odin.Core.Util;
using Odin.Services.Apps;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives.Reactions;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Incoming.Reactions;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Reactions;

/// <summary/>
public class PeerReactionSenderService(
    IOdinHttpClientFactory odinHttpClientFactory,
    CircleNetworkService circleNetworkService,
    
    FileSystemResolver fileSystemResolver,
    OdinConfiguration odinConfiguration)
    : PeerServiceBase(odinHttpClientFactory,
        circleNetworkService, fileSystemResolver)
{

    /// <summary />
    public async Task AddReaction(OdinId odinId, AddRemoteReactionRequest request, IOdinContext odinContext, DatabaseConnection cn)
    {
        var (token, client) = await CreateReactionContentClient(odinId, odinContext, cn);

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

        AssertValidResponse(response);
    }

    /// <summary />
    public async Task<GetReactionsPerimeterResponse> GetReactions(OdinId odinId, GetRemoteReactionsRequest request, IOdinContext odinContext, DatabaseConnection cn)
    {
        var (token, client) = await CreateReactionContentClient(odinId,odinContext, cn);
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
    public async Task<GetReactionCountsResponse> GetReactionCounts(OdinId odinId, GetRemoteReactionsRequest request, IOdinContext odinContext, DatabaseConnection cn)
    {
        var (token, client) = await CreateReactionContentClient(odinId,odinContext, cn);
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

    public async Task<List<string>> GetReactionsByIdentityAndFile(OdinId odinId, PeerGetReactionsByIdentityRequest request, IOdinContext odinContext, DatabaseConnection cn)
    {
        var (token, client) = await CreateReactionContentClient(odinId,odinContext, cn);
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

    public async Task DeleteReaction(OdinId odinId, DeleteReactionRequestByGlobalTransitId request, IOdinContext odinContext, DatabaseConnection cn)
    {
        var (token, client) = await CreateReactionContentClient(odinId,odinContext, cn);
        SharedSecretEncryptedTransitPayload payload = this.CreateSharedSecretEncryptedPayload(token, request);
        try
        {
            // ApiResponse<HttpContent> response = null;
            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.PeerOperationMaxAttempts,
                odinConfiguration.Host.PeerOperationDelayMs,
                CancellationToken.None,
                async () => { await client.DeleteReactionContent(payload); });
        }
        catch (TryRetryException ex)
        {
            HandleTryRetryException(ex);
            throw;
        }
    }

    public async Task DeleteAllReactions(OdinId odinId, DeleteReactionRequestByGlobalTransitId request, IOdinContext odinContext, DatabaseConnection cn)
    {
        var (token, client) = await CreateReactionContentClient(odinId, odinContext, cn);
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
}