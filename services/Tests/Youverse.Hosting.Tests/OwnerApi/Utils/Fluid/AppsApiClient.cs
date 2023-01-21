using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;
using Youverse.Hosting.Controllers.OwnerToken.AppManagement;
using Youverse.Hosting.Tests.OwnerApi.Apps;

namespace Youverse.Hosting.Tests.OwnerApi.Utils.Fluid;

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

        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
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

            var (tokenPortableBytes, sharedSecret) = ByteArrayUtil.Split(decryptedData, 33, 16);

            ClientAuthenticationToken authenticationResult = ClientAuthenticationToken.FromPortableBytes(tokenPortableBytes);

            Assert.False(authenticationResult.Id == Guid.Empty);
            Assert.IsNotNull(authenticationResult.AccessTokenHalfKey);
            Assert.That(authenticationResult.AccessTokenHalfKey.GetKey().Length, Is.EqualTo(16));
            Assert.IsTrue(authenticationResult.AccessTokenHalfKey.IsSet());

            Assert.IsNotNull(sharedSecret);
            Assert.That(sharedSecret.Length, Is.EqualTo(16));

            return (authenticationResult, sharedSecret);
        }
    }

    public async Task RevokeApp(Guid appId)
    {
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
        {
            var svc = RefitCreator.RestServiceFor<IAppRegistrationClient>(client, ownerSharedSecret);

            await svc.RevokeApp(new GetAppRequest() { AppId = appId });
        }
    }

    public async Task UpdateAppAuthorizedCircles(Guid appId, List<Guid> authorizedCircles, PermissionSetGrantRequest grant)
    {
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
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
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
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
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
        {
            var svc = RefitCreator.RestServiceFor<IAppRegistrationClient>(client, ownerSharedSecret);

            var request = new AppRegistrationRequest
            {
                Name = $"Test_{appId}",
                AppId = appId,
                PermissionSet = appPermissions.PermissionSet,
                Drives = appPermissions.Drives.ToList(),
                AuthorizedCircles = authorizedCircles,
                CircleMemberGrantRequest = circleMemberGrantRequest
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
}