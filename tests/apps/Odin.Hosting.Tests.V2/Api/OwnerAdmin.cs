#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Util;
using Odin.Hosting.Controllers.OwnerToken.AppManagement;
using Odin.Hosting.Controllers.OwnerToken.Membership.YouAuth;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;
using Odin.Hosting.Tests;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.Configuration;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.DriveManagement;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.YouAuth;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Apps;
using Odin.Services.Authentication.YouAuth;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.YouAuth;
using Refit;

namespace Odin.Hosting.Tests.V2.Api;

/// <summary>
/// V1 admin operations (drives, apps, circles, YouAuth domains) routed over the in-process pipeline
/// as the logged-in owner. V2 doesn't yet expose admin endpoints for these, so test setup uses V1
/// here even though the SUT calls in test bodies stay V2.
/// </summary>
public sealed class OwnerAdmin
{
    private readonly OwnerSession _owner;
    private readonly UniversalCircleNetworkApiClient _network;

    internal OwnerAdmin(OwnerSession owner)
    {
        _owner = owner;
        _network = new UniversalCircleNetworkApiClient(owner.Identity, owner.Factory);
    }

    // -----------------------------------------------------------------------------------------
    // Tenant initialization (one-time per identity — creates system circles + system drives)
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Runs the tenant initial-setup flow (system circles + system drives + any optional extras).
    /// Required for the peer connection-request flow to grant the <c>ConfirmedConnections</c>
    /// system circle. Idempotent on the server, so safe to call from per-fixture warm-up. Throws
    /// on non-2xx — every helper in this file throws (test setup that fails is always a broken
    /// test, never an expected outcome).
    /// </summary>
    public async Task<ApiResponse<bool>> InitializeIdentity(InitialSetupRequest? request = null)
    {
        var (client, ss) = _owner.NewAdminHttpClient();
        var svc = RefitCreator.RestServiceFor<IRefitOwnerConfiguration>(client, ss);
        var response = await svc.InitializeIdentity(request ?? new InitialSetupRequest());
        EnsureSuccess(response, nameof(InitializeIdentity));
        return response;
    }

    // -----------------------------------------------------------------------------------------
    // Drives
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Creates a drive on the owner's identity. Defaults match the V2-test convention of
    /// anonymous-readable, non-owner-only, no subscriptions; callers typically pass these through
    /// from <see cref="DriveSpec"/>.
    /// </summary>
    public async Task<ApiResponse<bool>> CreateDrive(
        TargetDrive drive,
        string name,
        bool allowAnonymousReads = true,
        bool ownerOnly = false,
        bool allowSubscriptions = false)
    {
        var (client, ss) = _owner.NewAdminHttpClient();
        var svc = RefitCreator.RestServiceFor<IRefitDriveManagement>(client, ss);
        var response = await svc.CreateDrive(new CreateDriveRequest
        {
            TargetDrive = drive,
            Name = name,
            Metadata = string.Empty,
            AllowAnonymousReads = allowAnonymousReads,
            AllowSubscriptions = allowSubscriptions,
            OwnerOnly = ownerOnly,
        });
        EnsureSuccess(response, nameof(CreateDrive));
        return response;
    }

    // -----------------------------------------------------------------------------------------
    // Circles (delegated to the existing new-style client; works with our factory unchanged)
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Creates a circle that members will be granted on connection. Used by <see cref="GuestSession"/>
    /// to attach a YouAuth domain to a drive-permission grant.
    /// </summary>
    public async Task<ApiResponse<HttpContent>> CreateCircle(Guid id, string name, PermissionSetGrantRequest grant)
    {
        var response = await _network.CreateCircle(id, name, grant);
        EnsureSuccess(response, nameof(CreateCircle));
        return response;
    }

    // -----------------------------------------------------------------------------------------
    // Apps
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Registers an app with the given permissions. Throws on non-2xx; callers in this framework
    /// always want this to succeed since failure here means the rest of the test setup is broken.
    /// </summary>
    public async Task<ApiResponse<RedactedAppRegistration>> RegisterApp(
        Guid appId,
        PermissionSetGrantRequest appPermissions,
        List<Guid>? authorizedCircles = null,
        PermissionSetGrantRequest? circleMemberGrantRequest = null)
    {
        var (client, ss) = _owner.NewAdminHttpClient();
        var svc = RefitCreator.RestServiceFor<IRefitOwnerAppRegistration>(client, ss);

        var response = await svc.RegisterApp(new AppRegistrationRequest
        {
            Name = $"Test_{appId}",
            AppId = appId,
            PermissionSet = appPermissions.PermissionSet,
            Drives = appPermissions.Drives?.ToList(),
            AuthorizedCircles = authorizedCircles ?? new List<Guid>(),
            CircleMemberPermissionGrant = circleMemberGrantRequest ?? new PermissionSetGrantRequest()
        });
        EnsureSuccess(response, nameof(RegisterApp));
        return response;
    }

    /// <summary>
    /// Registers a client for an already-registered app and runs the ECC + AES-CBC exchange to
    /// extract the issued client auth token and shared secret. Mirrors
    /// <c>AppManagementApiClient.RegisterAppClient</c>.
    /// </summary>
    public async Task<(ClientAuthenticationToken token, byte[] sharedSecret)> RegisterAppClient(Guid appId)
    {
        var (client, ss) = _owner.NewAdminHttpClient();
        var svc = RefitCreator.RestServiceFor<IRefitOwnerAppRegistration>(client, ss);

        var clientPrivateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
        var clientKeyPair = new EccFullKeyData(clientPrivateKey, EccKeySize.P384, 1);

        var regResponse = await svc.RegisterAppOnClientUsingEcc(new AppClientRegistrationRequest
        {
            AppId = appId,
            JwkBase64UrlPublicKey = clientKeyPair.PublicKeyJwkBase64Url(),
            ClientFriendlyName = "test in-process app client"
        });
        EnsureSuccess(regResponse, nameof(RegisterAppClient) + ".RegisterAppOnClientUsingEcc");
        if (regResponse.Content == null)
        {
            throw new InvalidOperationException(
                $"{nameof(RegisterAppClient)}.RegisterAppOnClientUsingEcc returned 2xx but empty content");
        }

        var reply = regResponse.Content;
        var remotePublicKey = EccPublicKeyData.FromJwkBase64UrlPublicKey(reply.ExchangePublicKeyJwkBase64Url);
        var remoteSalt = Convert.FromBase64String(reply.ExchangeSalt64);

        var exchangeSecret = clientKeyPair.GetEcdhSharedSecret(clientPrivateKey, remotePublicKey, remoteSalt);
        var exchangeSecretDigest = SHA256.Create().ComputeHash(exchangeSecret.GetKey()).ToBase64();

        var tokenResp = await svc.ExchangeDigestForToken(new YouAuthTokenRequest { SecretDigest = exchangeSecretDigest });
        EnsureSuccess(tokenResp, nameof(RegisterAppClient) + ".ExchangeDigestForToken");
        if (tokenResp.Content == null)
        {
            throw new InvalidOperationException(
                $"{nameof(RegisterAppClient)}.ExchangeDigestForToken returned 2xx but empty content");
        }

        var token = tokenResp.Content;
        var sharedSecret = AesCbc.Decrypt(
            Convert.FromBase64String(token.Base64SharedSecretCipher!),
            exchangeSecret,
            Convert.FromBase64String(token.Base64SharedSecretIv!));
        var authBytes = AesCbc.Decrypt(
            Convert.FromBase64String(token.Base64ClientAuthTokenCipher!),
            exchangeSecret,
            Convert.FromBase64String(token.Base64ClientAuthTokenIv!));

        var authToken = ClientAuthenticationToken.FromPortableBytes(authBytes);
        return (authToken, sharedSecret);
    }

    // -----------------------------------------------------------------------------------------
    // YouAuth domains (Guest setup)
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Registers a YouAuth domain (the third-party domain that a Guest test caller represents),
    /// optionally granting it the given circles. Throws on non-2xx.
    /// </summary>
    public async Task<ApiResponse<RedactedYouAuthDomainRegistration>> RegisterYouAuthDomain(
        AsciiDomainName domain,
        List<GuidId>? circleIds = null)
    {
        var (client, ss) = _owner.NewAdminHttpClient();
        var svc = RefitCreator.RestServiceFor<IRefitYouAuthDomainRegistration>(client, ss);
        var response = await svc.RegisterDomain(new YouAuthDomainRegistrationRequest
        {
            Name = $"Test_{domain.DomainName}",
            Domain = domain.DomainName,
            CircleIds = circleIds ?? new List<GuidId>(),
            ConsentRequirements = new ConsentRequirements { ConsentRequirementType = ConsentRequirementType.Never }
        });
        EnsureSuccess(response, nameof(RegisterYouAuthDomain));
        return response;
    }

    /// <summary>
    /// Registers a client under an already-registered YouAuth domain. The returned access token
    /// and shared secret are what the <see cref="GuestSession"/> uses to authenticate.
    /// </summary>
    public async Task<ApiResponse<YouAuthDomainClientRegistrationResponse>> RegisterYouAuthClient(
        AsciiDomainName domain,
        string friendlyName = "test in-process guest client")
    {
        var (client, ss) = _owner.NewAdminHttpClient();
        var svc = RefitCreator.RestServiceFor<IRefitYouAuthDomainRegistration>(client, ss);
        var response = await svc.RegisterClient(new YouAuthDomainClientRegistrationRequest
        {
            Domain = domain.DomainName,
            ClientFriendlyName = friendlyName
        });
        EnsureSuccess(response, nameof(RegisterYouAuthClient));
        return response;
    }

    private static void EnsureSuccess<T>(ApiResponse<T> response, string opName)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"{opName} failed: {(int)response.StatusCode} {response.StatusCode}" +
                (response.Error?.Content is { Length: > 0 } body ? $" — {body}" : ""));
        }
    }
}
