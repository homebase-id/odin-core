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
using Odin.Hosting.Tests._Universal.ApiClient.Owner.DriveManagement;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.YouAuth;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Apps;
using Odin.Services.Authentication.YouAuth;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
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
    // Drives
    // -----------------------------------------------------------------------------------------

    public async Task<ApiResponse<bool>> CreateDrive(TargetDrive drive, string name, bool allowAnonymousReads = true)
    {
        var (client, ss) = _owner.NewAdminHttpClient();
        var svc = RefitCreator.RestServiceFor<IRefitDriveManagement>(client, ss);
        return await svc.CreateDrive(new CreateDriveRequest
        {
            TargetDrive = drive,
            Name = name,
            Metadata = string.Empty,
            AllowAnonymousReads = allowAnonymousReads,
            AllowSubscriptions = false,
            OwnerOnly = false,
        });
    }

    // -----------------------------------------------------------------------------------------
    // Circles (delegated to the existing new-style client; works with our factory unchanged)
    // -----------------------------------------------------------------------------------------

    public Task<ApiResponse<HttpContent>> CreateCircle(Guid id, string name, PermissionSetGrantRequest grant) =>
        _network.CreateCircle(id, name, grant);

    // -----------------------------------------------------------------------------------------
    // Apps
    // -----------------------------------------------------------------------------------------

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
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"RegisterApp failed: {response.StatusCode}");
        }
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
        if (!regResponse.IsSuccessStatusCode || regResponse.Content == null)
        {
            throw new InvalidOperationException($"RegisterAppOnClientUsingEcc failed: {regResponse.StatusCode}");
        }

        var reply = regResponse.Content;
        var remotePublicKey = EccPublicKeyData.FromJwkBase64UrlPublicKey(reply.ExchangePublicKeyJwkBase64Url);
        var remoteSalt = Convert.FromBase64String(reply.ExchangeSalt64);

        var exchangeSecret = clientKeyPair.GetEcdhSharedSecret(clientPrivateKey, remotePublicKey, remoteSalt);
        var exchangeSecretDigest = SHA256.Create().ComputeHash(exchangeSecret.GetKey()).ToBase64();

        var tokenResp = await svc.ExchangeDigestForToken(new YouAuthTokenRequest { SecretDigest = exchangeSecretDigest });
        if (!tokenResp.IsSuccessStatusCode || tokenResp.Content == null)
        {
            throw new InvalidOperationException($"ExchangeDigestForToken failed: {tokenResp.StatusCode}");
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
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"RegisterDomain failed: {response.StatusCode}");
        }
        return response;
    }

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
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"RegisterClient failed: {response.StatusCode}");
        }
        return response;
    }
}
