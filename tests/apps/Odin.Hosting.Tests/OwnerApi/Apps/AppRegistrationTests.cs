using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Hosting.Controllers.OwnerToken.AppManagement;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Apps;

namespace Odin.Hosting.Tests.OwnerApi.Apps
{
    public class AppRegistrationTests
    {
        // private TestScaffold _scaffold;

        private WebScaffold _scaffold;

        private readonly TestIdentity _identity = TestIdentities.Frodo;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var folder = GetType().Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>() { TestIdentities.Frodo });
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }

        [SetUp]
        public void Setup()
        {
            _scaffold.ClearAssertLogEventsAction();
            _scaffold.ClearLogEvents();
        }

        [TearDown]
        public void TearDown()
        {
            _scaffold.AssertLogEvents();
        }


        [Test]
        public async Task RegisterNewApp()
        {
            var appId = Guid.NewGuid();
            var name = "API Tests Sample App-register";
            var corsHostName = "photos.odin.earth";

            await AddSampleAppNoDrive(appId, name, corsHostName);
        }


        [Test]
        public async Task RegisterNewAppWith_UseTransitWrite_HasReadWriteOnTransientTempDrive_and_ICR_Key()
        {
            var applicationId = Guid.NewGuid();
            var name = "App with Use Transit Read Access";

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
            {
                var svc = _scaffold.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);
                var request = new AppRegistrationRequest
                {
                    AppId = applicationId,
                    Name = name,
                    PermissionSet = new PermissionSet(new List<int>() { PermissionKeys.UseTransitWrite }),
                    Drives = null,
                    CorsHostName = default
                };

                var response = await svc.RegisterApp(request);
                ClassicAssert.IsTrue(response.IsSuccessStatusCode);

                var appResponse = await svc.GetRegisteredApp(new GetAppRequest() { AppId = applicationId });
                ClassicAssert.IsTrue(appResponse.IsSuccessStatusCode, $"Could not retrieve the app {applicationId}");

                var registeredApp = appResponse.Content;
                ClassicAssert.IsNotNull(registeredApp, "App should exist");

                ClassicAssert.IsTrue(registeredApp.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitWrite),
                    "App should have use transit read permission");
                ClassicAssert.IsTrue(registeredApp.Grant.HasIcrKey, "missing icr key but UseTransit is true");

                var transientDriveGrant = registeredApp.Grant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive.Drive ==
                    SystemDriveConstants.TransientTempDrive);
                ClassicAssert.IsNotNull(transientDriveGrant);
                ClassicAssert.IsTrue(transientDriveGrant!.PermissionedDrive.Permission.HasFlag(DrivePermission.ReadWrite));
            }
        }


        [Test]
        public async Task RegisterNewAppWith_UseTransitRead_HasReadWriteOnTransientTempDrive_and_ICR_Key()
        {
            var applicationId = Guid.NewGuid();
            var name = "App with Use Transit Read Access";

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
            {
                var svc = _scaffold.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);
                var request = new AppRegistrationRequest
                {
                    AppId = applicationId,
                    Name = name,
                    PermissionSet = new PermissionSet(new List<int>() { PermissionKeys.UseTransitRead }),
                    Drives = null,
                    CorsHostName = default
                };

                var response = await svc.RegisterApp(request);
                ClassicAssert.IsTrue(response.IsSuccessStatusCode);

                var appResponse = await svc.GetRegisteredApp(new GetAppRequest() { AppId = applicationId });
                ClassicAssert.IsTrue(appResponse.IsSuccessStatusCode, $"Could not retrieve the app {applicationId}");

                var registeredApp = appResponse.Content;
                ClassicAssert.IsNotNull(registeredApp, "App should exist");

                ClassicAssert.IsTrue(registeredApp.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitRead),
                    "App should have use transit read permission");
                ClassicAssert.IsTrue(registeredApp.Grant.HasIcrKey, "missing icr key but UseTransit is true");

                var transientDriveGrant = registeredApp.Grant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive.Drive ==
                    SystemDriveConstants.TransientTempDrive);
                ClassicAssert.IsNotNull(transientDriveGrant);
                ClassicAssert.IsTrue(transientDriveGrant!.PermissionedDrive.Permission.HasFlag(DrivePermission.ReadWrite));
            }
        }

        [Test]
        public async Task RegisterNewApp_Without_UseTransitWrite_HasNoIcrKey_AndDoesNotHavePermissionOn_TransientTempDrive()
        {
            var applicationId = Guid.NewGuid();
            var name = "App with Use Transit Access";

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
            {
                var svc = _scaffold.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);
                var request = new AppRegistrationRequest
                {
                    AppId = applicationId,
                    Name = name,
                    PermissionSet = new PermissionSet(new List<int>()),
                    Drives = null,
                    CorsHostName = default
                };

                var response = await svc.RegisterApp(request);
                ClassicAssert.IsTrue(response.IsSuccessStatusCode);

                var appResponse = await svc.GetRegisteredApp(new GetAppRequest() { AppId = applicationId });
                ClassicAssert.IsTrue(appResponse.IsSuccessStatusCode, $"Could not retrieve the app {applicationId}");

                var registeredApp = appResponse.Content;
                ClassicAssert.IsNotNull(registeredApp, "App should exist");

                ClassicAssert.IsFalse(registeredApp.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitWrite),
                    "App should not have UseTransit");
                ClassicAssert.IsFalse(registeredApp.Grant.HasIcrKey,
                    "Icr key should not be present when UseTransit permission is not given");

                var transientDriveGrant = registeredApp.Grant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive.Drive ==
                    SystemDriveConstants.TransientTempDrive);
                ClassicAssert.IsNull(transientDriveGrant);
            }
        }


        [Test]
        public async Task RegisterNewApp_Without_UseTransitRead_HasNoIcrKey_AndDoesNotHavePermissionOn_TransientTempDrive()
        {
            var applicationId = Guid.NewGuid();
            var name = "App with Use Transit Access";

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
            {
                var svc = _scaffold.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);
                var request = new AppRegistrationRequest
                {
                    AppId = applicationId,
                    Name = name,
                    PermissionSet = new PermissionSet(new List<int>()),
                    Drives = null,
                    CorsHostName = default
                };

                var response = await svc.RegisterApp(request);
                ClassicAssert.IsTrue(response.IsSuccessStatusCode);

                var appResponse = await svc.GetRegisteredApp(new GetAppRequest() { AppId = applicationId });
                ClassicAssert.IsTrue(appResponse.IsSuccessStatusCode, $"Could not retrieve the app {applicationId}");

                var registeredApp = appResponse.Content;
                ClassicAssert.IsNotNull(registeredApp, "App should exist");

                ClassicAssert.IsFalse(registeredApp.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitRead),
                    "App should not have UseTransit");
                ClassicAssert.IsFalse(registeredApp.Grant.HasIcrKey,
                    "Icr key should not be present when UseTransit permission is not given");

                var transientDriveGrant = registeredApp.Grant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive.Drive ==
                    SystemDriveConstants.TransientTempDrive);
                ClassicAssert.IsNull(transientDriveGrant);
            }
        }

        [Test]
        public async Task AppPermissionUpdate_Keeps_TransientDriveWhen_UseTransitWrite_IsGranted()
        {
            var applicationId = Guid.NewGuid();

            var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
            var appPermissionsGrant = new PermissionSetGrantRequest()
            {
                Drives = null,
                PermissionSet = new PermissionSet([])
            };

            await frodoOwnerClient.Apps.RegisterApp(applicationId, appPermissionsGrant);

            //
            // Should not have icr or transient temp drive
            //
            var appReg = await frodoOwnerClient.Apps.GetAppRegistration(applicationId);
            ClassicAssert.IsNotNull(appReg);
            ClassicAssert.IsFalse(appReg.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitWrite));
            ClassicAssert.IsFalse(appReg.Grant.HasIcrKey);
            ClassicAssert.IsNull(
                appReg.Grant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive.Drive == SystemDriveConstants.TransientTempDrive));

            appPermissionsGrant.PermissionSet = new PermissionSet(new List<int>() { PermissionKeys.UseTransitWrite });
            await frodoOwnerClient.Apps.UpdateAppPermissions(applicationId, appPermissionsGrant);

            var updatedAppReg = await frodoOwnerClient.Apps.GetAppRegistration(applicationId);
            ClassicAssert.IsNotNull(updatedAppReg);
            ClassicAssert.IsTrue(updatedAppReg.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitWrite));
            ClassicAssert.IsTrue(updatedAppReg.Grant.HasIcrKey);

            var transientDriveGrant = updatedAppReg.Grant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive.Drive ==
                                                                                            SystemDriveConstants.TransientTempDrive);
            ClassicAssert.IsNotNull(transientDriveGrant);
        }

        [Test]
        public async Task AppPermissionUpdate_Keeps_TransientDriveWhen_UseTransitRead_IsGranted()
        {
            var applicationId = Guid.NewGuid();

            var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
            var appPermissionsGrant = new PermissionSetGrantRequest()
            {
                Drives = null,
                PermissionSet = new PermissionSet(new List<int>())
            };

            await frodoOwnerClient.Apps.RegisterApp(applicationId, appPermissionsGrant);

            //
            // Should not have icr or transient temp drive
            //
            var appReg = await frodoOwnerClient.Apps.GetAppRegistration(applicationId);
            ClassicAssert.IsNotNull(appReg);
            ClassicAssert.IsFalse(appReg.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitRead));
            ClassicAssert.IsFalse(appReg.Grant.HasIcrKey);
            ClassicAssert.IsNull(
                appReg.Grant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive.Drive == SystemDriveConstants.TransientTempDrive));

            appPermissionsGrant.PermissionSet = new PermissionSet(new List<int> { PermissionKeys.UseTransitRead });
            await frodoOwnerClient.Apps.UpdateAppPermissions(applicationId, appPermissionsGrant);

            var updatedAppReg = await frodoOwnerClient.Apps.GetAppRegistration(applicationId);
            ClassicAssert.IsNotNull(updatedAppReg);
            ClassicAssert.IsTrue(updatedAppReg.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitRead));
            ClassicAssert.IsTrue(updatedAppReg.Grant.HasIcrKey);

            var transientDriveGrant = updatedAppReg.Grant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive.Drive ==
                                                                                            SystemDriveConstants.TransientTempDrive);
            ClassicAssert.IsNotNull(transientDriveGrant);
        }

        [Test]
        public async Task RevokingUseTransitWrite_RemovesIcrKey_And_TransientTempDrive()
        {
            var applicationId = Guid.NewGuid();

            var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
            var appPermissionsGrant = new PermissionSetGrantRequest()
            {
                Drives = null,
                PermissionSet = new PermissionSet(new List<int>() { PermissionKeys.UseTransitWrite })
            };

            await frodoOwnerClient.Apps.RegisterApp(applicationId, appPermissionsGrant);

            var appReg = await frodoOwnerClient.Apps.GetAppRegistration(applicationId);
            ClassicAssert.IsNotNull(appReg);
            ClassicAssert.IsTrue(appReg.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitWrite));
            ClassicAssert.IsTrue(appReg.Grant.HasIcrKey);

            appPermissionsGrant.PermissionSet = new PermissionSet(); //remove use transit
            await frodoOwnerClient.Apps.UpdateAppPermissions(applicationId, appPermissionsGrant);

            var updatedAppReg = await frodoOwnerClient.Apps.GetAppRegistration(applicationId);
            ClassicAssert.IsNotNull(updatedAppReg);
            ClassicAssert.IsFalse(updatedAppReg.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitWrite));
            ClassicAssert.IsFalse(updatedAppReg.Grant.HasIcrKey);

            var transientDriveGrant = updatedAppReg.Grant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive.Drive ==
                                                                                            SystemDriveConstants.TransientTempDrive);
            ClassicAssert.IsNull(transientDriveGrant);
        }

        [Test]
        public async Task RevokingUseTransitRead_RemovesIcrKey_And_TransientTempDrive()
        {
            var applicationId = Guid.NewGuid();

            var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
            var appPermissionsGrant = new PermissionSetGrantRequest()
            {
                Drives = null,
                PermissionSet = new PermissionSet(new List<int>() { PermissionKeys.UseTransitRead })
            };

            await frodoOwnerClient.Apps.RegisterApp(applicationId, appPermissionsGrant);

            var appReg = await frodoOwnerClient.Apps.GetAppRegistration(applicationId);
            ClassicAssert.IsNotNull(appReg);
            ClassicAssert.IsTrue(appReg.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitRead));
            ClassicAssert.IsTrue(appReg.Grant.HasIcrKey);

            appPermissionsGrant.PermissionSet = new PermissionSet(); //remove use transit
            await frodoOwnerClient.Apps.UpdateAppPermissions(applicationId, appPermissionsGrant);

            var updatedAppReg = await frodoOwnerClient.Apps.GetAppRegistration(applicationId);
            ClassicAssert.IsNotNull(updatedAppReg);
            ClassicAssert.IsFalse(updatedAppReg.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitRead));
            ClassicAssert.IsFalse(updatedAppReg.Grant.HasIcrKey);

            var transientDriveGrant = updatedAppReg.Grant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive.Drive ==
                                                                                            SystemDriveConstants.TransientTempDrive);
            ClassicAssert.IsNull(transientDriveGrant);
        }

        // [Test]
        // public async Task CanRotateIcrKey()
        // {
        //    
        // }

        [Test]
        public async Task FailToRegisterNewAppWithInvalidCorsHostName()
        {
            var applicationId = Guid.NewGuid();
            var name = "API Tests Sample App-register";
            var corsHostName = "*.odin.earth";

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
            {
                var svc = _scaffold.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);
                var request = new AppRegistrationRequest
                {
                    AppId = applicationId,
                    Name = name,
                    PermissionSet = null,
                    Drives = null,
                    CorsHostName = corsHostName
                };

                var response = await svc.RegisterApp(request);

                ClassicAssert.IsFalse(response.IsSuccessStatusCode,
                    $"Should have failed to add app registration.  Status code was {response.StatusCode}");

                var appResponse = await svc.GetRegisteredApp(new GetAppRequest() { AppId = applicationId });
                ClassicAssert.IsTrue(appResponse.IsSuccessStatusCode, $"Could not retrieve the app {applicationId}");
                ClassicAssert.IsNull(appResponse.Content, "There should be no app");
            }
        }

        [Test]
        public async Task RegisterNewAppWithCorsHostNameAndPort()
        {
            var applicationId = Guid.NewGuid();
            var name = "API Tests Sample App-register";
            var corsHostName = "somewhere.odin.earth:444";

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
            {
                var svc = _scaffold.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);
                var request = new AppRegistrationRequest
                {
                    AppId = applicationId,
                    Name = name,
                    PermissionSet = null,
                    Drives = null,
                    CorsHostName = corsHostName
                };

                var response = await svc.RegisterApp(request);

                ClassicAssert.IsTrue(response.IsSuccessStatusCode, "Status code was {response.StatusCode}");

                var appResponse = await svc.GetRegisteredApp(new GetAppRequest() { AppId = applicationId });
                ClassicAssert.IsTrue(appResponse.IsSuccessStatusCode, $"Could not retrieve the app {applicationId}");
                ClassicAssert.IsNotNull(appResponse.Content, "There should be no app");
                ClassicAssert.IsTrue(appResponse.Content!.CorsHostName == corsHostName);
            }
        }

        [Test]
        public async Task FailToRegisterNewAppWithCorsHostNameAndInvalidPort()
        {
            var applicationId = Guid.NewGuid();
            var name = "API Tests Sample App-register";
            var corsHostName = "somewhere.odin.earth:3AC";

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
            {
                var svc = _scaffold.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);
                var request = new AppRegistrationRequest
                {
                    AppId = applicationId,
                    Name = name,
                    PermissionSet = null,
                    Drives = null,
                    CorsHostName = corsHostName
                };

                var response = await svc.RegisterApp(request);

                ClassicAssert.IsFalse(response.IsSuccessStatusCode, "Status code was {response.StatusCode}");

                var appResponse = await svc.GetRegisteredApp(new GetAppRequest() { AppId = applicationId });
                ClassicAssert.IsTrue(appResponse.IsSuccessStatusCode, $"Could not retrieve the app {applicationId}");
                ClassicAssert.IsNull(appResponse.Content, "There should be no app");
            }
        }

        [Test]
        public async Task RegisterNewAppWithDriveAndPermissions()
        {
            var applicationId = Guid.NewGuid();
            var name = "API TestApp";

            var targetDrive1 = TargetDrive.NewTargetDrive();
            await _scaffold.OldOwnerApi.CreateDrive(_identity.OdinId, targetDrive1, "Drive 1 for Circle Test", "", false);

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
            var circle1Definition = await _scaffold.OldOwnerApi.CreateCircleWithDrive(_identity.OdinId, "Circle 1", circle1PermissionKeys,
                circle1Drives);

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(_identity.OdinId, out var ownerSharedSecret);
            {
                var svc = _scaffold.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);
                var request = new AppRegistrationRequest
                {
                    AppId = applicationId,
                    Name = name,
                    Drives = new List<DriveGrantRequest>() { dgr1, dgr2 },
                    PermissionSet = new PermissionSet(new List<int>()
                        { PermissionKeys.ReadCircleMembership, PermissionKeys.ReadConnections }),
                    AuthorizedCircles = new List<Guid>() { circle1Definition.Id },
                    CircleMemberPermissionGrant = new PermissionSetGrantRequest()
                    {
                        Drives = new List<DriveGrantRequest>() { dgr2 },
                        PermissionSet = new PermissionSet(new List<int>() { PermissionKeys.ReadConnections })
                    }
                };

                var response = await svc.RegisterApp(request);

                ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var appReg = response.Content;
                ClassicAssert.IsNotNull(appReg);

                var savedApp = await GetSampleApp(applicationId);
                ClassicAssert.IsTrue(savedApp.AppId == request.AppId);
                ClassicAssert.IsTrue(savedApp.Name == request.Name);

                CollectionAssert.AreEquivalent(savedApp.AuthorizedCircles, request.AuthorizedCircles);

                CollectionAssert.AreEquivalent(savedApp.Grant.DriveGrants.Select(d => d.PermissionedDrive).ToList(),
                    request.Drives.Select(p => p.PermissionedDrive));
                ClassicAssert.IsTrue(savedApp.Grant.PermissionSet == request.PermissionSet);

                CollectionAssert.AreEquivalent(
                    savedApp.CircleMemberPermissionSetGrantRequest.Drives.Select(d => d.PermissionedDrive).ToList(),
                    request.CircleMemberPermissionGrant.Drives.Select(p => p.PermissionedDrive).ToList());
                ClassicAssert.IsTrue(savedApp.CircleMemberPermissionSetGrantRequest.PermissionSet ==
                                     request.CircleMemberPermissionGrant.PermissionSet);
            }
        }

        [Test]
        public async Task RevokeAppRegistration()
        {
            var appId = Guid.NewGuid();
            var name = "API Tests Sample App-revoke";
            var corsHostName = "photos.odin.earth";

            await AddSampleAppNoDrive(appId, name, corsHostName);

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
            {
                var svc = _scaffold.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);
                var revokeResponse = await svc.RevokeApp(new GetAppRequest() { AppId = appId });

                ClassicAssert.IsTrue(revokeResponse.IsSuccessStatusCode);
                ClassicAssert.IsTrue(revokeResponse.Content?.Success);

                var savedApp = await GetSampleApp(appId);
                ClassicAssert.IsNotNull(savedApp);
                ClassicAssert.IsTrue(savedApp.IsRevoked);
            }
        }

        [Test]
        public async Task RegisterAppOnClient()
        {
            var appId = Guid.NewGuid();
            var name = "API Tests Sample App-reg-app-device";
            var corsHostName = "app.somewhere.org";
            await AddSampleAppNoDrive(appId, name, corsHostName);

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(_identity.OdinId, out var ownerSharedSecret);
            {
                var svc = _scaffold.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);

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
            }
        }

        [Test]
        public async Task UpdateAppPermissions()
        {
            var applicationId = Guid.NewGuid();
            var name = "API TestApp";

            var targetDrive1 = TargetDrive.NewTargetDrive();
            await _scaffold.OldOwnerApi.CreateDrive(_identity.OdinId, targetDrive1, "Drive 1 for Circle Test", "", false);

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

            var dgr3 = new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = targetDrive1,
                    Permission = DrivePermission.ReadWrite
                }
            };

            var circle1PermissionKeys = new List<int>() { PermissionKeys.ReadCircleMembership, PermissionKeys.ReadConnections };
            var circle1Drives = new List<PermissionedDrive>() { new() { Drive = targetDrive1, Permission = DrivePermission.Read } };
            var circle1Definition = await _scaffold.OldOwnerApi.CreateCircleWithDrive(_identity.OdinId, "Circle 1", circle1PermissionKeys,
                circle1Drives);

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(_identity.OdinId, out var ownerSharedSecret);
            {
                var svc = _scaffold.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);
                var request = new AppRegistrationRequest
                {
                    AppId = applicationId,
                    Name = name,
                    Drives = new List<DriveGrantRequest>() { dgr1, dgr2 },
                    PermissionSet = new PermissionSet(new List<int>()
                        { PermissionKeys.ReadCircleMembership, PermissionKeys.ReadConnections }),
                    AuthorizedCircles = new List<Guid>() { circle1Definition.Id },
                    CircleMemberPermissionGrant = new PermissionSetGrantRequest()
                    {
                        Drives = new List<DriveGrantRequest>() { dgr2 },
                        PermissionSet = new PermissionSet(new List<int>() { PermissionKeys.ReadConnections })
                    },
                };

                var response = await svc.RegisterApp(request);

                ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var appReg = response.Content;
                ClassicAssert.IsNotNull(appReg);

                var savedApp = await GetSampleApp(applicationId);
                ClassicAssert.IsTrue(savedApp.AppId == request.AppId);
                ClassicAssert.IsTrue(savedApp.Name == request.Name);

                CollectionAssert.AreEquivalent(savedApp.AuthorizedCircles, request.AuthorizedCircles);

                CollectionAssert.AreEquivalent(savedApp.Grant.DriveGrants.Select(d => d.PermissionedDrive).ToList(),
                    request.Drives.Select(p => p.PermissionedDrive));
                ClassicAssert.IsTrue(savedApp.Grant.PermissionSet == request.PermissionSet);

                CollectionAssert.AreEquivalent(
                    savedApp.CircleMemberPermissionSetGrantRequest.Drives.Select(d => d.PermissionedDrive).ToList(),
                    request.CircleMemberPermissionGrant.Drives.Select(p => p.PermissionedDrive).ToList());
                ClassicAssert.IsTrue(savedApp.CircleMemberPermissionSetGrantRequest.PermissionSet ==
                                     request.CircleMemberPermissionGrant.PermissionSet);

                var updateRequest = new UpdateAppPermissionsRequest()
                {
                    AppId = applicationId,
                    PermissionSet = new PermissionSet(new List<int>() { PermissionKeys.ReadConnectionRequests }),
                    Drives = new List<DriveGrantRequest>() { dgr3 }
                };

                await svc.UpdateAppPermissions(updateRequest);

                var updatedApp = await GetSampleApp(applicationId);
                // be sure the permissions are updated 
                CollectionAssert.AreEquivalent(updatedApp.Grant.DriveGrants.Select(d => d.PermissionedDrive).ToList(),
                    updateRequest.Drives.Select(p => p.PermissionedDrive));
                ClassicAssert.IsTrue(updatedApp.Grant.PermissionSet == updateRequest.PermissionSet);

                // be sure the other fields did not change
                CollectionAssert.AreEquivalent(updatedApp.AuthorizedCircles, request.AuthorizedCircles);
                CollectionAssert.AreEquivalent(
                    updatedApp.CircleMemberPermissionSetGrantRequest.Drives.Select(d => d.PermissionedDrive).ToList(),
                    request.CircleMemberPermissionGrant.Drives.Select(p => p.PermissionedDrive).ToList());
                ClassicAssert.IsTrue(updatedApp.CircleMemberPermissionSetGrantRequest.PermissionSet ==
                                     request.CircleMemberPermissionGrant.PermissionSet);
            }
        }

        [Test]
        public async Task UpdateAuthorizedCircles()
        {
            var applicationId = Guid.NewGuid();
            var name = "API TestApp";

            var targetDrive1 = TargetDrive.NewTargetDrive();
            await _scaffold.OldOwnerApi.CreateDrive(_identity.OdinId, targetDrive1, "Drive 1 for Circle Test", "", false);

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

            var dgr3 = new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = targetDrive1,
                    Permission = DrivePermission.ReadWrite
                }
            };

            var circle1PermissionKeys = new List<int>() { PermissionKeys.ReadCircleMembership, PermissionKeys.ReadConnections };
            var circle1Drives = new List<PermissionedDrive>() { new() { Drive = targetDrive1, Permission = DrivePermission.Read } };
            var circle1Definition = await _scaffold.OldOwnerApi.CreateCircleWithDrive(_identity.OdinId, "Circle 1", circle1PermissionKeys,
                circle1Drives);

            var circle2PermissionKeys = new List<int>() { PermissionKeys.ReadConnections };
            var circle2Drives = new List<PermissionedDrive>() { new() { Drive = targetDrive1, Permission = DrivePermission.Write } };
            var circle2Definition = await _scaffold.OldOwnerApi.CreateCircleWithDrive(_identity.OdinId, "Circle 2", circle2PermissionKeys,
                circle2Drives);

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(_identity.OdinId, out var ownerSharedSecret);
            {
                var svc = _scaffold.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);
                var request = new AppRegistrationRequest
                {
                    AppId = applicationId,
                    Name = name,
                    Drives = new List<DriveGrantRequest>() { dgr1, dgr2 },
                    PermissionSet = new PermissionSet(new List<int>()
                        { PermissionKeys.ReadCircleMembership, PermissionKeys.ReadConnections }),
                    AuthorizedCircles = new List<Guid>() { circle1Definition.Id },
                    CircleMemberPermissionGrant = new PermissionSetGrantRequest()
                    {
                        Drives = new List<DriveGrantRequest>() { dgr2 },
                        PermissionSet = new PermissionSet(new List<int>() { PermissionKeys.ReadConnections })
                    }
                };

                var response = await svc.RegisterApp(request);

                ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var appReg = response.Content;
                ClassicAssert.IsNotNull(appReg);

                var savedApp = await GetSampleApp(applicationId);
                ClassicAssert.IsTrue(savedApp.AppId == request.AppId);
                ClassicAssert.IsTrue(savedApp.Name == request.Name);

                CollectionAssert.AreEquivalent(savedApp.AuthorizedCircles, request.AuthorizedCircles);

                CollectionAssert.AreEquivalent(savedApp.Grant.DriveGrants.Select(d => d.PermissionedDrive).ToList(),
                    request.Drives.Select(p => p.PermissionedDrive));
                ClassicAssert.IsTrue(savedApp.Grant.PermissionSet == request.PermissionSet);

                CollectionAssert.AreEquivalent(
                    savedApp.CircleMemberPermissionSetGrantRequest.Drives.Select(d => d.PermissionedDrive).ToList(),
                    request.CircleMemberPermissionGrant.Drives.Select(p => p.PermissionedDrive).ToList());
                ClassicAssert.IsTrue(savedApp.CircleMemberPermissionSetGrantRequest.PermissionSet ==
                                     request.CircleMemberPermissionGrant.PermissionSet);

                var updateRequest = new UpdateAuthorizedCirclesRequest()
                {
                    AppId = applicationId,
                    AuthorizedCircles = new List<Guid>() { circle2Definition.Id },
                    CircleMemberPermissionGrant = new PermissionSetGrantRequest()
                    {
                        PermissionSet = new PermissionSet(new List<int>() { PermissionKeys.ReadConnectionRequests }),
                        Drives = new List<DriveGrantRequest>() { dgr3 }
                    }
                };

                await svc.UpdateAuthorizedCircles(updateRequest);

                var updatedApp = await GetSampleApp(applicationId);
                // be sure the permissions are updated 
                CollectionAssert.AreEquivalent(updatedApp.AuthorizedCircles, updateRequest.AuthorizedCircles);
                CollectionAssert.AreEquivalent(
                    updatedApp.CircleMemberPermissionSetGrantRequest.Drives.Select(d => d.PermissionedDrive).ToList(),
                    updateRequest.CircleMemberPermissionGrant.Drives.Select(p => p.PermissionedDrive).ToList());

                ClassicAssert.IsTrue(updatedApp.CircleMemberPermissionSetGrantRequest.PermissionSet ==
                                     updateRequest.CircleMemberPermissionGrant.PermissionSet);
                // be sure the other fields did not change

                CollectionAssert.AreEquivalent(updatedApp.Grant.DriveGrants.Select(d => d.PermissionedDrive).ToList(),
                    request.Drives.Select(p => p.PermissionedDrive));
                ClassicAssert.IsTrue(updatedApp.Grant.PermissionSet == request.PermissionSet);
            }
        }

        private async Task<RedactedAppRegistration> AddSampleAppNoDrive(Guid applicationId, string name, string corsHostName)
        {
            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
            {
                var svc = _scaffold.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);
                var request = new AppRegistrationRequest
                {
                    AppId = applicationId,
                    Name = name,
                    PermissionSet = null,
                    Drives = null,
                    CorsHostName = corsHostName
                };

                var response = await svc.RegisterApp(request);

                ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var appReg = response.Content;
                ClassicAssert.IsNotNull(appReg);

                var savedApp = await GetSampleApp(applicationId);
                ClassicAssert.IsTrue(savedApp.AppId == request.AppId);
                ClassicAssert.IsTrue(savedApp.Name == request.Name);
                ClassicAssert.IsTrue(savedApp.CorsHostName == request.CorsHostName);

                return appReg;
            }
        }

        private async Task<RedactedAppRegistration> GetSampleApp(Guid appId)
        {
            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
            {
                var svc = _scaffold.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);
                var appResponse = await svc.GetRegisteredApp(new GetAppRequest() { AppId = appId });
                ClassicAssert.IsTrue(appResponse.IsSuccessStatusCode, $"Could not retrieve the app {appId}");
                ClassicAssert.IsNotNull(appResponse.Content, $"Could not retrieve the app {appId}");
                var savedApp = appResponse.Content;
                return savedApp;
            }
        }
    }
}