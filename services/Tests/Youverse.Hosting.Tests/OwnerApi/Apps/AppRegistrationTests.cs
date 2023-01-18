using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Controllers.OwnerToken.AppManagement;

namespace Youverse.Hosting.Tests.OwnerApi.Apps
{
    public class AppRegistrationTests
    {
        // private TestScaffold _scaffold;

        private WebScaffold _scaffold;

        private readonly TestIdentity _identity = TestIdentities.Frodo;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }

        [Test]
        public async Task RegisterNewApp()
        {
            var appId = Guid.NewGuid();
            var name = "API Tests Sample App-register";
            var newId = await AddSampleAppNoDrive(appId, name);
        }

        [Test]
        public async Task RegisterNewAppWithDriveAndPermissions()
        {
            var applicationId = Guid.NewGuid();
            var name = "API TestApp";

            var targetDrive1 = TargetDrive.NewTargetDrive();
            await _scaffold.OwnerApi.CreateDrive(_identity.DotYouId, targetDrive1, "Drive 1 for Circle Test", "", false);

            var dgr1 = new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = targetDrive1,
                    Permission = DrivePermission.ReadWrite
                }
            };

            var dgr2 = new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = targetDrive1,
                    Permission = DrivePermission.Write
                }
            };

            var circle1PermissionKeys = new List<int>() { PermissionKeys.ReadCircleMembership, PermissionKeys.ReadConnections };
            var circle1Drives = new List<PermissionedDrive>() { new() { Drive = targetDrive1, Permission = DrivePermission.Read } };
            var circle1Definition = await _scaffold.OwnerApi.CreateCircleWithDrive(_identity.DotYouId, "Circle 1", circle1PermissionKeys, circle1Drives);

            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(_identity.DotYouId, out var ownerSharedSecret))
            {
                var svc = _scaffold.RestServiceFor<IAppRegistrationClient>(client, ownerSharedSecret);
                var request = new AppRegistrationRequest
                {
                    AppId = applicationId,
                    Name = name,
                    Drives = new List<DriveGrantRequest>() { dgr1, dgr2 },
                    PermissionSet = new PermissionSet(new List<int>() { PermissionKeys.ReadCircleMembership, PermissionKeys.ReadConnections }),
                    AuthorizedCircles = new List<Guid>() { circle1Definition.Id },
                    CircleMemberDrives = new List<DriveGrantRequest>() { dgr2 },
                    CircleMemberPermissionSet = new PermissionSet(new List<int>() { PermissionKeys.ReadConnections })
                };

                var response = await svc.RegisterApp(request);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var appReg = response.Content;
                Assert.IsNotNull(appReg);

                var savedApp = await GetSampleApp(applicationId);
                Assert.IsTrue(savedApp.AppId == request.AppId);
                Assert.IsTrue(savedApp.Name == request.Name);

                CollectionAssert.AreEquivalent(savedApp.AuthorizedCircles, request.AuthorizedCircles);

                CollectionAssert.AreEquivalent(savedApp.Grant.DriveGrants.Select(d => d.PermissionedDrive).ToList(), request.Drives.Select(p => p.PermissionedDrive));
                Assert.IsTrue(savedApp.Grant.PermissionSet == request.PermissionSet);

                CollectionAssert.AreEquivalent(savedApp.CircleMemberGrant.DriveGrants.Select(d => d.PermissionedDrive).ToList(), request.CircleMemberDrives.Select(p => p.PermissionedDrive).ToList());
                Assert.IsTrue(savedApp.CircleMemberGrant.PermissionSet == request.CircleMemberPermissionSet);
            }
        }

        [Test]
        public async Task UpdateAppDriveAndPermissions()
        {
            var applicationId = Guid.NewGuid();
            var name = "API TestApp";

            var targetDrive1 = TargetDrive.NewTargetDrive();
            await _scaffold.OwnerApi.CreateDrive(_identity.DotYouId, targetDrive1, "Drive 1 for Circle Test", "", false);

            var dgr1 = new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = targetDrive1,
                    Permission = DrivePermission.ReadWrite
                }
            };

            var dgr2 = new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = targetDrive1,
                    Permission = DrivePermission.Write
                }
            };

            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(_identity.DotYouId, out var ownerSharedSecret))
            {
                var svc = _scaffold.RestServiceFor<IAppRegistrationClient>(client, ownerSharedSecret);
                var request = new AppRegistrationRequest
                {
                    AppId = applicationId,
                    Name = name,
                    Drives = new List<DriveGrantRequest>() { dgr1, dgr2 },
                    PermissionSet = new PermissionSet(new List<int>() { PermissionKeys.ReadCircleMembership, PermissionKeys.ReadConnections }),
                    AuthorizedCircles = new List<Guid>(),
                    CircleMemberDrives = new List<DriveGrantRequest>() { dgr2 },
                    CircleMemberPermissionSet = new PermissionSet(new List<int>() { PermissionKeys.ReadConnections })
                };

                var response = await svc.RegisterApp(request);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var appReg = response.Content;
                Assert.IsNotNull(appReg);

                var savedApp = await GetSampleApp(applicationId);
                Assert.IsTrue(savedApp.AppId == request.AppId);
                Assert.IsTrue(savedApp.Name == request.Name);

                CollectionAssert.AreEquivalent(savedApp.AuthorizedCircles, request.AuthorizedCircles);

                CollectionAssert.AreEquivalent(savedApp.Grant.DriveGrants.Select(d => d.PermissionedDrive).ToList(), request.Drives.Select(p => p.PermissionedDrive));
                Assert.IsTrue(savedApp.Grant.PermissionSet == request.PermissionSet);

                CollectionAssert.AreEquivalent(savedApp.CircleMemberGrant.DriveGrants.Select(d => d.PermissionedDrive).ToList(), request.CircleMemberDrives.Select(p => p.PermissionedDrive).ToList());
                Assert.IsTrue(savedApp.CircleMemberGrant.PermissionSet == request.CircleMemberPermissionSet);
            }

            //
            // Now update it
            //

            // 1. Add an authorized circle
            // 2. add a new drive
        }

        [Test]
        public async Task RevokeAppRegistration()
        {
            var appId = Guid.NewGuid();
            var name = "API Tests Sample App-revoke";

            var newId = await AddSampleAppNoDrive(appId, name);

            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
            {
                var svc = _scaffold.RestServiceFor<IAppRegistrationClient>(client, ownerSharedSecret);
                var revokeResponse = await svc.RevokeApp(new GetAppRequest() { AppId = appId });

                Assert.IsTrue(revokeResponse.IsSuccessStatusCode);
                Assert.IsTrue(revokeResponse.Content?.Success);

                var savedApp = await GetSampleApp(appId);
                Assert.IsNotNull(savedApp);
                Assert.IsTrue(savedApp.IsRevoked);
            }
        }

        [Test]
        public async Task RegisterAppOnClient()
        {
            var rsa = new RsaFullKeyData(ref RsaKeyListManagement.zeroSensitiveKey, 1);
            var appId = Guid.NewGuid();
            var name = "API Tests Sample App-reg-app-device";

            await AddSampleAppNoDrive(appId, name);

            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(_identity.DotYouId, out var ownerSharedSecret))
            {
                var svc = _scaffold.RestServiceFor<IAppRegistrationClient>(client, ownerSharedSecret);

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
            }
        }

        private async Task<RedactedAppRegistration> AddSampleAppNoDrive(Guid applicationId, string name)
        {
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
            {
                var svc = _scaffold.RestServiceFor<IAppRegistrationClient>(client, ownerSharedSecret);
                var request = new AppRegistrationRequest
                {
                    AppId = applicationId,
                    Name = name,
                    PermissionSet = null,
                    Drives = null
                };

                var response = await svc.RegisterApp(request);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var appReg = response.Content;
                Assert.IsNotNull(appReg);

                var savedApp = await GetSampleApp(applicationId);
                Assert.IsTrue(savedApp.AppId == request.AppId);
                Assert.IsTrue(savedApp.Name == request.Name);

                return appReg;
            }
        }

        private async Task<RedactedAppRegistration> GetSampleApp(Guid appId)
        {
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
            {
                var svc = _scaffold.RestServiceFor<IAppRegistrationClient>(client, ownerSharedSecret);
                var appResponse = await svc.GetRegisteredApp(new GetAppRequest() { AppId = appId });
                Assert.IsTrue(appResponse.IsSuccessStatusCode, $"Could not retrieve the app {appId}");
                Assert.IsNotNull(appResponse.Content, $"Could not retrieve the app {appId}");
                var savedApp = appResponse.Content;
                return savedApp;
            }
        }
    }
}