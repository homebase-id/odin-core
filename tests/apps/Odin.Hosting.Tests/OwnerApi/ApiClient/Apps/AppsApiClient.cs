using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Hosting.Controllers.OwnerToken.AppManagement;
using Odin.Hosting.Tests.OwnerApi.Apps;
using Odin.Hosting.Tests.OwnerApi.Utils;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient.Apps;

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
        var rsa = new RsaFullKeyData(RsaKeyListManagement.zeroSensitiveKey, 1); // TODO

        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);

            var request = new AppClientRegistrationRequest()
            {
                AppId = appId,
                ClientPublicKey64 = Convert.ToBase64String(rsa.publicKey),
                ClientFriendlyName = "Some phone"
            };

            var regResponse = await svc.RegisterAppOnClient(request);
            ClassicAssert.IsTrue(regResponse.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(regResponse.Content);

            var reply = regResponse.Content;
            var decryptedData = rsa.Decrypt(RsaKeyListManagement.zeroSensitiveKey, reply.Data); // TODO

            //only supporting version 1 for now
            Assert.That(reply.EncryptionVersion, Is.EqualTo(1));
            Assert.That(reply.Token, Is.Not.EqualTo(Guid.Empty));
            Assert.That(decryptedData, Is.Not.Null);
            Assert.That(decryptedData.Length, Is.EqualTo(49));

            var cat = ClientAccessToken.FromPortableBytes(decryptedData);
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

    public async Task RevokeApp(Guid appId)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);

            await svc.RevokeApp(new GetAppRequest() { AppId = appId });
        }
    }

    public async Task UpdateAppAuthorizedCircles(Guid appId, List<Guid> authorizedCircles, PermissionSetGrantRequest grant)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);

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
            var svc = RefitCreator.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);

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
            var svc = RefitCreator.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);

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

            return updatedAppResponse.Content;
        }
    }

    public async Task<RedactedAppRegistration> GetAppRegistration(Guid appId)
    {
        var client = this._ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);
            var appResponse = await svc.GetRegisteredApp(new GetAppRequest() { AppId = appId });
            ClassicAssert.IsTrue(appResponse.IsSuccessStatusCode, $"Could not retrieve the app {appId}");
            return appResponse.Content;
        }
    }
    
    public async Task<List<RegisteredAppClientResponse>> GetRegisteredClients(GuidId appId)
    {
        var client = this._ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);
            var appResponse = await svc.GetRegisteredClients(new GetAppRequest(){AppId = appId});
            ClassicAssert.IsTrue(appResponse.IsSuccessStatusCode, $"Could not retrieve the app clients");
            return appResponse.Content;
        }
    }
}