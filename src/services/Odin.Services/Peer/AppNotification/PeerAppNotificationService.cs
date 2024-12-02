using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Util;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
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
    private readonly TableKeyThreeValue _tableKeyThreeValue;
    private readonly PushNotificationService _pushNotificationService;
    private readonly ThreeKeyValueStorage _notificationSubscriptionStorage;

    private readonly byte[] _subscriptionsCategoryKey = Guid.Parse("3265f455-7fac-4569-8637-500119b4ae9d").ToByteArray();

    public PeerAppNotificationService(IOdinHttpClientFactory odinHttpClientFactory,
        OdinConfiguration odinConfiguration,
        CircleNetworkService circleNetworkService,
        TableKeyThreeValue tableKeyThreeValue,
        OdinConfiguration config,
        PushNotificationService pushNotificationService,
        FileSystemResolver fileSystemResolver) : base(odinHttpClientFactory, circleNetworkService, fileSystemResolver, odinConfiguration)
    {
        _odinConfiguration = odinConfiguration;
        _tableKeyThreeValue = tableKeyThreeValue;
        _pushNotificationService = pushNotificationService;
        _cache = new(config.Host.CacheSlidingExpirationSeconds);

        const string subscriptionContextKey = "e6981b6c-360f-477e-83e3-ed1f5be35209";
        _notificationSubscriptionStorage = TenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(subscriptionContextKey));
    }

    public async Task<PeerTransferResponse> EnqueuePushNotification(PushNotificationOutboxRecord record, IOdinContext odinContext)
    {
        odinContext.Caller.AssertCallerIsAuthenticated();

        var caller = odinContext.GetCallerOdinIdOrFail();

        var isConnected = (await CircleNetworkService.GetIcrAsync(caller, odinContext, true)).IsConnected();
        if (!isConnected)
        {
            throw new OdinSecurityException("Caller not connected");
        }

        var subscriptions = await GetSubscriptionsByIdentityInternal(caller);
        if (subscriptions.All(sub => sub.SubscriptionId != record.Options.PeerSubscriptionId))
        {
            throw new OdinSecurityException("Invalid subscription Id");
        }

        var newContext = OdinContextUpgrades.UpgradeToPeerTransferContext(odinContext);
        await _pushNotificationService.EnqueueNotification(record.SenderId, record.Options, newContext);

        return new PeerTransferResponse
        {
            Code = PeerResponseCode.AcceptedIntoInbox
        };
    }

    /// <summary>
    /// Allows a peer to send this identity push notifications
    /// </summary>
    public async Task SubscribePeerAsync(PeerNotificationSubscription request, IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotEmptyGuid(request.SubscriptionId, nameof(request.SubscriptionId));
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);

        await _notificationSubscriptionStorage.UpsertAsync(_tableKeyThreeValue, request.ToKey(), request.Identity.ToHashId().ToByteArray(),
            _subscriptionsCategoryKey, request);
    }


    /// <summary>
    /// Revokes a peer identity from sending this identity push notifications
    /// </summary>
    public async Task UnsubscribePeerAsync(PeerNotificationSubscription request, IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotEmptyGuid(request.SubscriptionId, nameof(request.SubscriptionId));
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendPushNotifications);

        await _notificationSubscriptionStorage.DeleteAsync(_tableKeyThreeValue, request.ToKey());
    }

    public async Task<List<PeerNotificationSubscription>> GetAllSubscriptions(IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.SendIntroductions);

        var list = await _notificationSubscriptionStorage.GetByCategoryAsync<PeerNotificationSubscription>(
            _tableKeyThreeValue,
            _subscriptionsCategoryKey);
        return list.ToList();
    }

    public async Task<SharedSecretEncryptedPayload> CreateNotificationToken(IOdinContext odinContext)
    {
        var token = await CircleNetworkService.CreatePeerIcrClientForCallerAsync(odinContext);
        var sharedSecret = odinContext.PermissionsContext.SharedSecretKey;

        return SharedSecretEncryptedPayload.Encrypt(token.ToPortableBytes(), sharedSecret);
    }

    /// <summary>
    /// Calls to a remote identity to get an <see cref="ClientAccessToken"/>
    /// </summary>
    public async Task<AppNotificationTokenResponse> GetRemoteNotificationToken(GetRemoteTokenRequest request, IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotNull(request.Identity, nameof(request.Identity));

        var (targetIdentityCat, client) = await CreateHttpClientAsync<IPeerAppNotificationHttpClient>(request.Identity, odinContext);

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
            AuthenticationToken64 = clientAccessToken.ToAuthenticationToken().ToPortableBytes64(),
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
                await CircleNetworkService.TryCreateConnectedYouAuthContextAsync(peerIcrClient.Identity, token, accessReg,
                    currentOdinContext);
            return odinContext;
        }

        var result = await _cache.GetOrAddContextAsync(token, Creator);
        return result;
    }

    private async Task<(bool isValid, PeerIcrClient icrClient)> ValidateClientAuthToken(ClientAuthenticationToken authToken,
        IOdinContext odinContext)
    {
        var peerIcrClient = await CircleNetworkService.GetPeerIcrClientAsync(authToken.Id);
        if (null == peerIcrClient)
        {
            return (false, null);
        }

        var icr = await CircleNetworkService.GetIcrAsync(peerIcrClient.Identity, odinContext, overrideHack: true);

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


    private async Task<List<PeerNotificationSubscription>> GetSubscriptionsByIdentityInternal(OdinId identity)
    {
        var list = await _notificationSubscriptionStorage.GetByKey2And3Async<PeerNotificationSubscription>(
            _tableKeyThreeValue,
            identity.ToHashId().ToByteArray(), _subscriptionsCategoryKey);
        return list.ToList();
    }
}