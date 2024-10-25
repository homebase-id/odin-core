using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Util;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.YouAuth;
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
    private readonly ExchangeGrantService _exchangeGrantService;

    /// <summary>
    /// Handles incoming reactions and queries from followers
    /// </summary>
    public PeerAppNotificationService(IOdinHttpClientFactory odinHttpClientFactory,
        OdinConfiguration odinConfiguration,
        CircleNetworkService circleNetworkService,
        TenantSystemStorage tenantSystemStorage,
        ExchangeGrantService exchangeGrantService,
        TenantContext tenantContext,
        OdinConfiguration config,
        FileSystemResolver fileSystemResolver) : base(odinHttpClientFactory, circleNetworkService, fileSystemResolver)
    {
        _odinConfiguration = odinConfiguration;
        _tenantSystemStorage = tenantSystemStorage;
        _exchangeGrantService = exchangeGrantService;

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

        return new AppNotificationTokenResponse()
        {
            // ClientAccessTokenBytes = ClientAccessToken.FromPortableBytes(portableBytes).ToPortableBytes()
            ClientAccessTokenBytes = portableBytes
        };
    }

    public async Task<IOdinContext> GetDotYouContext(ClientAuthenticationToken token, IOdinContext currentOdinContext)
    {
        async Task<IOdinContext> Creator()
        {
            var (isValid, accessReg, domainRegistration) = await ValidateClientAuthToken(token, currentOdinContext);

            if (!isValid || null == domainRegistration || accessReg == null)
            {
                throw new OdinSecurityException("Invalid token");
            }

            var odinId = (OdinId)domainRegistration.Domain.DomainName;
            var odinContext = await CircleNetworkService.TryCreateConnectedYouAuthContext(odinId, token, accessReg, currentOdinContext);
            if (null != odinContext)
            {
                return odinContext;
            }
            
            // TODO: should i use create transit permissions instead?
//            CircleNetworkService.CreateTransitPermissionContext(odinId,???)

            return await CreateAuthenticatedContextForYouAuthDomain(token, domainRegistration, accessReg, currentOdinContext);
        }

        var result = await _cache.GetOrAddContext(token, Creator);
        return result;
    }

    private async Task<(bool isValid, AccessRegistration? accessReg, PeerIcrClient? youAuthDomainRegistration)>
        ValidateClientAuthToken(ClientAuthenticationToken authToken, IOdinContext odinContext)
    {
        var peerIcrClient = await CircleNetworkService.GetPeerIcrClient(authToken.Id);
        if (null == peerIcrClient)
        {
            return (false, null, null);
        }
        //
        // var reg = await CircleNetworkService.GetIdentityConnectionRegistration(peerIcrClient.Identity, odinContext);
        //
        // if (null == reg)
        // {
        //     return (false, null, null);
        // }

        if (peerIcrClient.AccessRegistration.IsRevoked) // || reg.IsRevoked
        {
            return (false, null, null);
        }

        return (true, peerIcrClient.AccessRegistration, null);
    }

    private async Task<IOdinContext> CreateAuthenticatedContextForYouAuthDomain(
        ClientAuthenticationToken authToken,
        YouAuthDomainRegistration domainRegistration,
        AccessRegistration accessReg,
        IOdinContext odinContext)
    {
        if (!string.IsNullOrEmpty(domainRegistration.CorsHostName))
        {
            //just in case something changed in the db record
            AppUtil.AssertValidCorsHeader(domainRegistration.CorsHostName);
        }

        var permissionKeys = _tenantContext.Settings.GetAdditionalPermissionKeysForAuthenticatedIdentities();
        var anonymousDrivePermissions = _tenantContext.Settings.GetAnonymousDrivePermissionsForAuthenticatedIdentities();

        var (grants, enabledCircles) =
            _circleMembershipService.MapCircleGrantsToExchangeGrants(domainRegistration.Domain,
                domainRegistration.CircleGrants.Values.ToList(),
                odinContext);

        var permissionContext = await _exchangeGrantService.CreatePermissionContext(
            authToken: authToken,
            grants: grants,
            accessReg: accessReg,
            odinContext: odinContext,
            db: _tenantSystemStorage.IdentityDatabase,
            additionalPermissionKeys: permissionKeys,
            includeAnonymousDrives: true,
            anonymousDrivePermission: anonymousDrivePermissions);

        var dotYouContext = new OdinContext()
        {
            Caller = new CallerContext(
                odinId: new OdinId(domainRegistration.Domain.DomainName),
                masterKey: null,
                securityLevel: SecurityGroupType.Authenticated,
                circleIds: enabledCircles,
                odinClientContext: new OdinClientContext()
                {
                    ClientIdOrDomain = domainRegistration.Domain.DomainName,
                    CorsHostName = domainRegistration.CorsHostName,
                    AccessRegistrationId = accessReg.Id,
                    DevicePushNotificationKey = null
                })
        };

        dotYouContext.SetPermissionContext(permissionContext);
        return dotYouContext;
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