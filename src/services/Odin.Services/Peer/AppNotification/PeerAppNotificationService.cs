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
public class PeerAppNotificationService : PeerServiceBase
{
    private readonly OdinContextCache _cache;
    private readonly OdinConfiguration _odinConfiguration;
    private readonly TenantSystemStorage _tenantSystemStorage;

    /// <summary>
    /// Handles incoming reactions and queries from followers
    /// </summary>
    public PeerAppNotificationService(IOdinHttpClientFactory odinHttpClientFactory,
        OdinConfiguration odinConfiguration,
        CircleNetworkService circleNetworkService,
        TenantSystemStorage tenantSystemStorage,
        OdinConfiguration config,
        FileSystemResolver fileSystemResolver) : base(odinHttpClientFactory, circleNetworkService, fileSystemResolver)
    {
        _odinConfiguration = odinConfiguration;
        _tenantSystemStorage = tenantSystemStorage;

        _cache = new OdinContextCache(config.Host.CacheSlidingExpirationSeconds);
    }


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
        var db = _tenantSystemStorage.IdentityDatabase;
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotNull(request.Identity, nameof(request.Identity));

        var (targetIdentityCat, client) = await CreateHttpClient<IPeerAppNotificationHttpClient>(request.Identity, db, odinContext);

        ApiResponse<SharedSecretEncryptedPayload> response = null;
        try
        {
            await TryRetry.WithDelayAsync(
                _odinConfiguration.Host.PeerOperationMaxAttempts,
                _odinConfiguration.Host.PeerOperationDelayMs,
                CancellationToken.None,
                async () => { response = await client.GetAppNotificationToken(); });
        }
        catch (TryRetryException ex)
        {
            HandleTryRetryException(ex);
            throw;
        }

        AssertValidResponse(response);

        var portableBytes = response.Content.Decrypt(targetIdentityCat.SharedSecret);

        var clientAccessToken = ClientAccessToken.FromPortableBytes(portableBytes);
        return new AppNotificationTokenResponse
        {
            AuthenticationToken64= clientAccessToken.ToAuthenticationToken().ToPortableBytes64(),
            SharedSecret = clientAccessToken.SharedSecret.GetKey()
        };
    }

    public async Task<IOdinContext> GetDotYouContext(ClientAuthenticationToken token, IOdinContext currentOdinContext)
    {
        async Task<IOdinContext> Creator()
        {
            var (isValid, peerIcrClient) = await ValidateClientAuthToken(token, currentOdinContext);

            if (!isValid || null == peerIcrClient)
            {
                return null;
            }

            var accessReg = peerIcrClient.AccessRegistration;
            var odinContext =
                await CircleNetworkService.TryCreateConnectedYouAuthContext(peerIcrClient.Identity, token, accessReg, currentOdinContext);
            return odinContext;
        }

        var result = await _cache.GetOrAddContext(token, Creator);
        return result;
    }

    private async Task<(bool isValid, PeerIcrClient icrClient)> ValidateClientAuthToken(ClientAuthenticationToken authToken,
        IOdinContext odinContext)
    {
        var peerIcrClient = await CircleNetworkService.GetPeerIcrClient(authToken.Id);
        if (null == peerIcrClient)
        {
            return (false, null);
        }

        var icr = await CircleNetworkService.GetIdentityConnectionRegistration(peerIcrClient.Identity, odinContext, overrideHack: true);

        if (!icr.IsConnected())
        {
            return (false, null);
        }

        if (peerIcrClient.AccessRegistration.IsRevoked) // || reg.IsRevoked
        {
            return (false, null);
        }

        return (true, peerIcrClient);
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