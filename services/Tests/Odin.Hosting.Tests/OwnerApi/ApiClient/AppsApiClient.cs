using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Services.Authorization.Apps;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Base;
using Odin.Hosting.Controllers.OwnerToken.AppManagement;
using Odin.Hosting.Tests.OwnerApi.Apps;
using Odin.Hosting.Tests.OwnerApi.Utils;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient;

public class AppsApiClient
{
    private readonly TestIdentity _identity;
    private readonly OwnerApiTestUtils _ownerApi;

    public AppsApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
    {
        _ownerApi = ownerApi;
        _identity = identity;
    }

    public async Task<(ClientAuthenticationToken clientAuthToken, byte[] sharedSecret)> RegisterAppClient(Guid appId)
    {
        var rsa = new RsaFullKeyData(ref RsaKeyListManagement.zeroSensitiveKey, 1); // TODO

        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IAppRegistrationClient>(client, ownerSharedSecret);

            var request = new AppClientRegistrationRequest()
            {
                AppId = appId,
                ClientPublicKey64 = Convert.ToBase64String(rsa.publicKey),
                ClientFriendlyName = "Some phone"
            };

            var regResponse = await svc.RegisterAppOnClient(request);
            Assert.IsTrue(regResponse.IsSuccessStatusCode);
            Assert.IsNotNull(regResponse.Content);

            var reply = regResponse.Content;
            var decryptedData = rsa.Decrypt(ref RsaKeyListManagement.zeroSensitiveKey, reply.Data); // TODO

            //only supporting version 1 for now
            Assert.That(reply.EncryptionVersion, Is.EqualTo(1));
            Assert.That(reply.Token, Is.Not.EqualTo(Guid.Empty));
            Assert.That(decryptedData, Is.Not.Null);
            Assert.That(decryptedData.Length, Is.EqualTo(49));

            var cat = ClientAccessToken.FromPortableBytes(decryptedData);
            Assert.IsFalse(cat.Id == Guid.Empty);
            Assert.IsNotNull(cat.AccessTokenHalfKey);
            Assert.That(cat.AccessTokenHalfKey.GetKey().Length, Is.EqualTo(16));
            Assert.IsTrue(cat.AccessTokenHalfKey.IsSet());
            Assert.IsTrue(cat.IsValid());

            Assert.IsNotNull(cat.SharedSecret);
            Assert.That(cat.SharedSecret.GetKey().Length, Is.EqualTo(16));

            return (cat.ToAuthenticationToken(), cat.SharedSecret.GetKey());
        }
    }

    public async Task RevokeApp(Guid appId)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IAppRegistrationClient>(client, ownerSharedSecret);

            await svc.RevokeApp(new GetAppRequest() { AppId = appId });
        }
    }

    public async Task UpdateAppAuthorizedCircles(Guid appId, List<Guid> authorizedCircles, PermissionSetGrantRequest grant)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IAppRegistrationClient>(client, ownerSharedSecret);

            await svc.UpdateAuthorizedCircles(new UpdateAuthorizedCirclesRequest()
            {
                AppId = appId,
                AuthorizedCircles = authorizedCircles,
                CircleMemberPermissionGrant = grant
            });
        }
    }

    public async Task UpdateAppPermissions(Guid appId, PermissionSetGrantRequest grant)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IAppRegistrationClient>(client, ownerSharedSecret);

            await svc.UpdateAppPermissions(new UpdateAppPermissionsRequest()
            {
                AppId = appId,
                Drives = grant.Drives,
                PermissionSet = grant.PermissionSet
            });
        }
    }

    /// <summary>
    /// Creates an app, device, and logs in returning an contextual information needed to run unit tests.
    /// </summary>
    /// <returns></returns>
    public async Task<RedactedAppRegistration> RegisterApp(
        Guid appId,
        PermissionSetGrantRequest appPermissions,
        List<Guid> authorizedCircles = null,
        PermissionSetGrantRequest circleMemberGrantRequest = null)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IAppRegistrationClient>(client, ownerSharedSecret);

            var request = new AppRegistrationRequest
            {
                Name = $"Test_{appId}",
                AppId = appId,
                PermissionSet = appPermissions.PermissionSet,
                Drives = appPermissions.Drives.ToList(),
                AuthorizedCircles = authorizedCircles,
                CircleMemberPermissionGrant = circleMemberGrantRequest
            };

            var response = await svc.RegisterApp(request);

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            var appReg = response.Content;
            Assert.IsNotNull(appReg);

            var updatedAppResponse = await svc.GetRegisteredApp(new GetAppRequest() { AppId = appId });
            Assert.That(updatedAppResponse.IsSuccessStatusCode, Is.True);
            Assert.That(updatedAppResponse.Content, Is.Not.Null);

            return updatedAppResponse.Content;
        }
    }

    public async Task<RedactedAppRegistration> GetAppRegistration(Guid appId)
    {
        var client = this._ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IAppRegistrationClient>(client, ownerSharedSecret);
            var appResponse = await svc.GetRegisteredApp(new GetAppRequest() { AppId = appId });
            Assert.IsTrue(appResponse.IsSuccessStatusCode, $"Could not retrieve the app {appId}");
            return appResponse.Content;
        }
    }
    
    public async Task<List<RegisteredAppClientResponse>> GetRegisteredClients()
    {
        var client = this._ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IAppRegistrationClient>(client, ownerSharedSecret);
            var appResponse = await svc.GetRegisteredClients();
            Assert.IsTrue(appResponse.IsSuccessStatusCode, $"Could not retrieve the app clients");
            return appResponse.Content;
        }
    }
}