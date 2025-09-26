using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Fluff;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Hosting.Controllers.OwnerToken.AppManagement;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Apps;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Owner.AppManagement;

public class AppManagementApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
{
    public async Task<(ClientAuthenticationToken clientAuthToken, byte[] sharedSecret)> RegisterAppClient(Guid appId)
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity.OdinId, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);

            var clientPrivateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            var clientKeyPair = new EccFullKeyData(clientPrivateKey, EccKeySize.P384, 1);

            var request = new AppClientEccRegistrationRequest()
            {
                AppId = appId,
                JwkBase64UrlPublicKey = clientKeyPair.PublicKeyJwkBase64Url(),
                ClientFriendlyName = "Some phone"
            };

            var regResponse = await svc.RegisterAppOnClientUsingEcc(request);
            ClassicAssert.IsTrue(regResponse.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(regResponse.Content);

            var reply = regResponse.Content;
            Assert.That(reply, Is.Not.Null);

            var remotePublicKey = EccPublicKeyData.FromJwkBase64UrlPublicKey(reply.ExchangePublicKeyJwkBase64Url);
            var remoteSalt = Convert.FromBase64String(reply.ExchangeSalt64);

            var exchangeSecret = clientKeyPair.GetEcdhSharedSecret(clientPrivateKey, remotePublicKey, remoteSalt);
            var exchangeSecretDigest = SHA256.Create().ComputeHash(exchangeSecret.GetKey()).ToBase64();
            Assert.That(reply.EncryptionVersion, Is.EqualTo(1));

            var youAuthResponse = await svc.ExchangeDigestForToken(new YouAuthTokenRequest()
            {
                SecretDigest = exchangeSecretDigest
            });

            var token = youAuthResponse.Content;
            Assert.That(token, Is.Not.Null);

            Assert.That(token.Base64SharedSecretCipher, Is.Not.Null.And.Not.Empty);
            Assert.That(token.Base64SharedSecretIv, Is.Not.Null.And.Not.Empty);
            Assert.That(token.Base64ClientAuthTokenCipher, Is.Not.Null.And.Not.Empty);
            Assert.That(token.Base64ClientAuthTokenIv, Is.Not.Null.And.Not.Empty);

            var sharedSecretCipher = Convert.FromBase64String(token.Base64SharedSecretCipher!);
            var sharedSecretIv = Convert.FromBase64String(token.Base64SharedSecretIv!);
            var sharedSecret = AesCbc.Decrypt(sharedSecretCipher, exchangeSecret, sharedSecretIv);
            Assert.That(sharedSecret, Is.Not.Null.And.Not.Empty);

            var clientAuthTokenCipher = Convert.FromBase64String(token.Base64ClientAuthTokenCipher!);
            var clientAuthTokenIv = Convert.FromBase64String(token.Base64ClientAuthTokenIv!);
            var clientAuthTokenBytes = AesCbc.Decrypt(clientAuthTokenCipher, exchangeSecret, clientAuthTokenIv);
            Assert.That(clientAuthTokenBytes, Is.Not.Null.And.Not.Empty);

            var authToken = ClientAuthenticationToken.FromPortableBytes(clientAuthTokenBytes);
            var cat = new ClientAccessToken
            {
                Id = authToken.Id,
                AccessTokenHalfKey = authToken.AccessTokenHalfKey,
                ClientTokenType = authToken.ClientTokenType,
                SharedSecret = sharedSecret.ToSensitiveByteArray()
            };

            ClassicAssert.IsFalse(cat.Id == Guid.Empty);
            ClassicAssert.IsNotNull(cat.AccessTokenHalfKey);
            Assert.That(cat.AccessTokenHalfKey.GetKey().Length, Is.EqualTo(16));
            ClassicAssert.IsTrue(cat.AccessTokenHalfKey.IsSet());
            ClassicAssert.IsTrue(cat.IsValid());

            ClassicAssert.IsNotNull(cat.SharedSecret);
            Assert.That(cat.SharedSecret.GetKey().Length, Is.EqualTo(16));

            return (cat.ToAuthenticationToken(), cat.SharedSecret.GetKey());
        }
    }

    public async Task<ApiResponse<NoResultResponse>> RevokeApp(Guid appId)
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitAppRegistration>(client, ownerSharedSecret);

            return await svc.RevokeApp(new GetAppRequest() { AppId = appId });
        }
    }

    public async Task<ApiResponse<HttpContent>> UpdateAppAuthorizedCircles(Guid appId, List<Guid> authorizedCircles,
        PermissionSetGrantRequest grant)
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitAppRegistration>(client, ownerSharedSecret);

            return await svc.UpdateAuthorizedCircles(new UpdateAuthorizedCirclesRequest()
            {
                AppId = appId,
                AuthorizedCircles = authorizedCircles,
                CircleMemberPermissionGrant = grant
            });
        }
    }

    public async Task<ApiResponse<HttpContent>> UpdateAppPermissions(Guid appId, PermissionSetGrantRequest grant)
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitAppRegistration>(client, ownerSharedSecret);

            return await svc.UpdateAppPermissions(new UpdateAppPermissionsRequest()
            {
                AppId = appId,
                Drives = grant.Drives,
                PermissionSet = grant.PermissionSet
            });
        }
    }

    public async Task<ClientAccessToken> RegisterAppAndClient(Guid appId,
        PermissionSetGrantRequest appPermissions,
        List<Guid> authorizedCircles = null,
        PermissionSetGrantRequest circleMemberGrantRequest = null)
    {
        var appRegResponse = await this.RegisterApp(appId, appPermissions, authorizedCircles, circleMemberGrantRequest);
        if (!appRegResponse.IsSuccessStatusCode)
        {
            throw new Exception("Failed to register app");
        }

        var appClient = await this.RegisterAppClient(appId);
        return new ClientAccessToken
        {
            Id = appClient.clientAuthToken.Id,
            AccessTokenHalfKey = appClient.clientAuthToken.AccessTokenHalfKey,
            ClientTokenType = appClient.clientAuthToken.ClientTokenType,
            SharedSecret = appClient.sharedSecret.ToSensitiveByteArray()
        };
    }

    /// <summary>
    /// Creates an app, device, and logs in returning an contextual information needed to run unit tests.
    /// </summary>
    /// <returns></returns>
    public async Task<ApiResponse<RedactedAppRegistration>> RegisterApp(
        Guid appId,
        PermissionSetGrantRequest appPermissions,
        List<Guid> authorizedCircles = null,
        PermissionSetGrantRequest circleMemberGrantRequest = null)
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitAppRegistration>(client, ownerSharedSecret);

            var request = new AppRegistrationRequest
            {
                Name = $"Test_{appId}",
                AppId = appId,
                PermissionSet = appPermissions.PermissionSet,
                Drives = appPermissions.Drives?.ToList(),
                AuthorizedCircles = authorizedCircles,
                CircleMemberPermissionGrant = circleMemberGrantRequest
            };

            var response = await svc.RegisterApp(request);

            ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            var appReg = response.Content;
            ClassicAssert.IsNotNull(appReg);

            var updatedAppResponse = await svc.GetRegisteredApp(new GetAppRequest() { AppId = appId });
            Assert.That(updatedAppResponse.IsSuccessStatusCode, Is.True);
            Assert.That(updatedAppResponse.Content, Is.Not.Null);

            return updatedAppResponse;
        }
    }

    public async Task<ApiResponse<RedactedAppRegistration>> GetAppRegistration(Guid appId)
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitAppRegistration>(client, ownerSharedSecret);
            var appResponse = await svc.GetRegisteredApp(new GetAppRequest() { AppId = appId });
            ClassicAssert.IsTrue(appResponse.IsSuccessStatusCode, $"Could not retrieve the app {appId}");
            return appResponse;
        }
    }

    public async Task<ApiResponse<List<RegisteredAppClientResponse>>> GetRegisteredClients(GuidId appId)
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitAppRegistration>(client, ownerSharedSecret);
            var appResponse = await svc.GetRegisteredClients(new GetAppRequest() { AppId = appId });
            ClassicAssert.IsTrue(appResponse.IsSuccessStatusCode, $"Could not retrieve the app clients");
            return appResponse;
        }
    }
}