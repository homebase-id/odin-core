#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Hosting.Controllers.OwnerToken.AppManagement;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;
using Odin.Hosting.Tests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Apps;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Refit;

namespace Odin.Hosting.Tests.V2.Api;

public sealed partial class OwnerAdmin
{
    /// <summary>
    /// Registers an app with the given permissions. Failure here means the rest of the test setup
    /// is broken, so we throw (see <see cref="EnsureSuccess{T}"/>).
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
}
