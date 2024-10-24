using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Util;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Membership.Connections;
using Odin.Services.Util;
using Refit;

namespace Odin.Services.Peer.AppNotification;

/// <summary>
/// Handles incoming reactions and queries from followers
/// </summary>
public class PeerAppNotificationService(
    IOdinHttpClientFactory odinHttpClientFactory,
    OdinConfiguration odinConfiguration,
    CircleNetworkService circleNetworkService,
    TenantSystemStorage tenantSystemStorage,
    FileSystemResolver fileSystemResolver)
    : PeerServiceBase(odinHttpClientFactory, circleNetworkService, fileSystemResolver)
{
    public async Task<SharedSecretEncryptedPayload> CreateNotificationToken(IOdinContext odinContext)
    {
        var token = await CircleNetworkService.CreatePeerIcrClientForCaller(odinContext);
        var sharedSecret = odinContext.PermissionsContext.SharedSecretKey;

        return SharedSecretEncryptedPayload.Encrypt(token.ToPortableBytes(), sharedSecret);
    }
    
    /// <summary>
    /// Calls to a remote identity to get an <see cref="ClientAccessToken"/> 
    /// </summary>
    public async Task<AppNotificationTokenResponse> GetRemoteNotificationToken(GetRemoteTokenRequest request, IOdinContext odinContext)
    {
        var db = tenantSystemStorage.IdentityDatabase;
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotNull(request.Identity, nameof(request.Identity));

        var (targetIdentityCat, client) = await CreateHttpClient<IPeerAppNotificationHttpClient>(request.Identity, db, odinContext);

        ApiResponse<SharedSecretEncryptedPayload> response = null;
        try
        {
            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.PeerOperationMaxAttempts,
                odinConfiguration.Host.PeerOperationDelayMs,
                CancellationToken.None,
                async () => { response = await client.GetAppNotificationToken(); });
        }
        catch (TryRetryException ex)
        {
            HandleTryRetryException(ex);
            throw;
        }

        AssertValidResponse(response);

        var bytes = response.Content.Decrypt(targetIdentityCat.SharedSecret);
        
        return new AppNotificationTokenResponse()
        {
            Token = ClientAccessToken.FromPortableBytes(bytes)
        };
    }

    private void HandleTryRetryException(TryRetryException ex)
    {
        var e = ex.InnerException;
        if (e is TaskCanceledException || e is HttpRequestException || e is OperationCanceledException)
        {
            throw new OdinClientException("Failed while calling remote identity", e);
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

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            throw new OdinClientException("Invalid Global TransitId", OdinClientErrorCode.InvalidGlobalTransitId);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new OdinSystemException($"Unhandled transit error response: {response.StatusCode}");
        }
    }
}