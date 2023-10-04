﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Services.Authorization.Apps;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Hosting.Controllers.OwnerToken.AppManagement;
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
            string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
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
            var corsHostName = "photos.odin.earth";

            var newId = await AddSampleAppNoDrive(appId, name, corsHostName);
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
                Assert.IsTrue(response.IsSuccessStatusCode);

                var appResponse = await svc.GetRegisteredApp(new GetAppRequest() { AppId = applicationId });
                Assert.IsTrue(appResponse.IsSuccessStatusCode, $"Could not retrieve the app {applicationId}");

                var registeredApp = appResponse.Content;
                Assert.IsNotNull(registeredApp, "App should exist");

                Assert.IsTrue(registeredApp.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitWrite), "App should have use transit read permission");
                Assert.IsTrue(registeredApp.Grant.HasIcrKey, "missing icr key but UseTransit is true");

                var transientDriveGrant = registeredApp.Grant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive.Drive == SystemDriveConstants.TransientTempDrive);
                Assert.IsNotNull(transientDriveGrant);
                Assert.IsTrue(transientDriveGrant.PermissionedDrive.Permission.HasFlag(DrivePermission.ReadWrite));
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
                Assert.IsTrue(response.IsSuccessStatusCode);

                var appResponse = await svc.GetRegisteredApp(new GetAppRequest() { AppId = applicationId });
                Assert.IsTrue(appResponse.IsSuccessStatusCode, $"Could not retrieve the app {applicationId}");

                var registeredApp = appResponse.Content;
                Assert.IsNotNull(registeredApp, "App should exist");

                Assert.IsTrue(registeredApp.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitRead), "App should have use transit read permission");
                Assert.IsTrue(registeredApp.Grant.HasIcrKey, "missing icr key but UseTransit is true");

                var transientDriveGrant = registeredApp.Grant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive.Drive == SystemDriveConstants.TransientTempDrive);
                Assert.IsNotNull(transientDriveGrant);
                Assert.IsTrue(transientDriveGrant.PermissionedDrive.Permission.HasFlag(DrivePermission.ReadWrite));
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
                Assert.IsTrue(response.IsSuccessStatusCode);

                var appResponse = await svc.GetRegisteredApp(new GetAppRequest() { AppId = applicationId });
                Assert.IsTrue(appResponse.IsSuccessStatusCode, $"Could not retrieve the app {applicationId}");

                var registeredApp = appResponse.Content;
                Assert.IsNotNull(registeredApp, "App should exist");

                Assert.IsFalse(registeredApp.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitWrite), "App should not have UseTransit");
                Assert.IsFalse(registeredApp.Grant.HasIcrKey, "Icr key should not be present when UseTransit permission is not given");

                var transientDriveGrant = registeredApp.Grant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive.Drive == SystemDriveConstants.TransientTempDrive);
                Assert.IsNull(transientDriveGrant);
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
                Assert.IsTrue(response.IsSuccessStatusCode);

                var appResponse = await svc.GetRegisteredApp(new GetAppRequest() { AppId = applicationId });
                Assert.IsTrue(appResponse.IsSuccessStatusCode, $"Could not retrieve the app {applicationId}");

                var registeredApp = appResponse.Content;
                Assert.IsNotNull(registeredApp, "App should exist");

                Assert.IsFalse(registeredApp.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitRead), "App should not have UseTransit");
                Assert.IsFalse(registeredApp.Grant.HasIcrKey, "Icr key should not be present when UseTransit permission is not given");

                var transientDriveGrant = registeredApp.Grant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive.Drive == SystemDriveConstants.TransientTempDrive);
                Assert.IsNull(transientDriveGrant);
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
                PermissionSet = new PermissionSet(new List<int>() { })
            };

            await frodoOwnerClient.Apps.RegisterApp(applicationId, appPermissionsGrant);

            //
            // Should not have icr or transient temp drive
            //
            var appReg = await frodoOwnerClient.Apps.GetAppRegistration(applicationId);
            Assert.IsNotNull(appReg);
            Assert.IsFalse(appReg.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitWrite));
            Assert.IsFalse(appReg.Grant.HasIcrKey);
            Assert.IsNull(appReg.Grant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive.Drive == SystemDriveConstants.TransientTempDrive));

            appPermissionsGrant.PermissionSet = new PermissionSet(new List<int>() {PermissionKeys.UseTransitWrite}); 
            await frodoOwnerClient.Apps.UpdateAppPermissions(applicationId, appPermissionsGrant);

            var updatedAppReg = await frodoOwnerClient.Apps.GetAppRegistration(applicationId);
            Assert.IsNotNull(updatedAppReg);
            Assert.IsTrue(updatedAppReg.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitWrite));
            Assert.IsTrue(updatedAppReg.Grant.HasIcrKey);

            var transientDriveGrant = updatedAppReg.Grant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive.Drive == SystemDriveConstants.TransientTempDrive);
            Assert.IsNotNull(transientDriveGrant);
        }

        [Test]
        public async Task AppPermissionUpdate_Keeps_TransientDriveWhen_UseTransitRead_IsGranted()
        {
            var applicationId = Guid.NewGuid();

            var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
            var appPermissionsGrant = new PermissionSetGrantRequest()
            {
                Drives = null,
                PermissionSet = new PermissionSet(new List<int>() { })
            };

            await frodoOwnerClient.Apps.RegisterApp(applicationId, appPermissionsGrant);

            //
            // Should not have icr or transient temp drive
            //
            var appReg = await frodoOwnerClient.Apps.GetAppRegistration(applicationId);
            Assert.IsNotNull(appReg);
            Assert.IsFalse(appReg.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitRead));
            Assert.IsFalse(appReg.Grant.HasIcrKey);
            Assert.IsNull(appReg.Grant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive.Drive == SystemDriveConstants.TransientTempDrive));

            appPermissionsGrant.PermissionSet = new PermissionSet(new List<int>() {PermissionKeys.UseTransitRead}); 
            await frodoOwnerClient.Apps.UpdateAppPermissions(applicationId, appPermissionsGrant);

            var updatedAppReg = await frodoOwnerClient.Apps.GetAppRegistration(applicationId);
            Assert.IsNotNull(updatedAppReg);
            Assert.IsTrue(updatedAppReg.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitRead));
            Assert.IsTrue(updatedAppReg.Grant.HasIcrKey);

            var transientDriveGrant = updatedAppReg.Grant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive.Drive == SystemDriveConstants.TransientTempDrive);
            Assert.IsNotNull(transientDriveGrant);
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
            Assert.IsNotNull(appReg);
            Assert.IsTrue(appReg.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitWrite));
            Assert.IsTrue(appReg.Grant.HasIcrKey);

            appPermissionsGrant.PermissionSet = new PermissionSet(); //remove use transit
            await frodoOwnerClient.Apps.UpdateAppPermissions(applicationId, appPermissionsGrant);

            var updatedAppReg = await frodoOwnerClient.Apps.GetAppRegistration(applicationId);
            Assert.IsNotNull(updatedAppReg);
            Assert.IsFalse(updatedAppReg.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitWrite));
            Assert.IsFalse(updatedAppReg.Grant.HasIcrKey);

            var transientDriveGrant = updatedAppReg.Grant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive.Drive == SystemDriveConstants.TransientTempDrive);
            Assert.IsNull(transientDriveGrant);
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
            Assert.IsNotNull(appReg);
            Assert.IsTrue(appReg.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitRead));
            Assert.IsTrue(appReg.Grant.HasIcrKey);

            appPermissionsGrant.PermissionSet = new PermissionSet(); //remove use transit
            await frodoOwnerClient.Apps.UpdateAppPermissions(applicationId, appPermissionsGrant);

            var updatedAppReg = await frodoOwnerClient.Apps.GetAppRegistration(applicationId);
            Assert.IsNotNull(updatedAppReg);
            Assert.IsFalse(updatedAppReg.Grant.PermissionSet.HasKey(PermissionKeys.UseTransitRead));
            Assert.IsFalse(updatedAppReg.Grant.HasIcrKey);

            var transientDriveGrant = updatedAppReg.Grant.DriveGrants.SingleOrDefault(dg => dg.PermissionedDrive.Drive == SystemDriveConstants.TransientTempDrive);
            Assert.IsNull(transientDriveGrant);
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

                Assert.IsFalse(response.IsSuccessStatusCode, $"Should have failed to add app registration.  Status code was {response.StatusCode}");
                var appReg = response.Content;

                var appResponse = await svc.GetRegisteredApp(new GetAppRequest() { AppId = applicationId });
                Assert.IsTrue(appResponse.IsSuccessStatusCode, $"Could not retrieve the app {applicationId}");
                Assert.IsNull(appResponse.Content, "There should be no app");
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

                Assert.IsTrue(response.IsSuccessStatusCode, "Status code was {response.StatusCode}");
                var appReg = response.Content;

                var appResponse = await svc.GetRegisteredApp(new GetAppRequest() { AppId = applicationId });
                Assert.IsTrue(appResponse.IsSuccessStatusCode, $"Could not retrieve the app {applicationId}");
                Assert.IsNotNull(appResponse.Content, "There should be no app");
                Assert.IsTrue(appResponse.Content.CorsHostName == corsHostName);
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

                Assert.IsFalse(response.IsSuccessStatusCode, "Status code was {response.StatusCode}");
                var appReg = response.Content;

                var appResponse = await svc.GetRegisteredApp(new GetAppRequest() { AppId = applicationId });
                Assert.IsTrue(appResponse.IsSuccessStatusCode, $"Could not retrieve the app {applicationId}");
                Assert.IsNull(appResponse.Content, "There should be no app");
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
            var circle1Definition = await _scaffold.OldOwnerApi.CreateCircleWithDrive(_identity.OdinId, "Circle 1", circle1PermissionKeys, circle1Drives);

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(_identity.OdinId, out var ownerSharedSecret);
            {
                var svc = _scaffold.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);
                var request = new AppRegistrationRequest
                {
                    AppId = applicationId,
                    Name = name,
                    Drives = new List<DriveGrantRequest>() { dgr1, dgr2 },
                    PermissionSet = new PermissionSet(new List<int>() { PermissionKeys.ReadCircleMembership, PermissionKeys.ReadConnections }),
                    AuthorizedCircles = new List<Guid>() { circle1Definition.Id },
                    CircleMemberPermissionGrant = new PermissionSetGrantRequest()
                    {
                        Drives = new List<DriveGrantRequest>() { dgr2 },
                        PermissionSet = new PermissionSet(new List<int>() { PermissionKeys.ReadConnections })
                    }
                };

                var response = await svc.RegisterApp(request);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var appReg = response.Content;
                Assert.IsNotNull(appReg);

                var savedApp = await GetSampleApp(applicationId);
                Assert.IsTrue(savedApp.AppId == request.AppId);
                Assert.IsTrue(savedApp.Name == request.Name);

                CollectionAssert.AreEquivalent(savedApp.AuthorizedCircles, request.AuthorizedCircles);

                CollectionAssert.AreEquivalent(savedApp.Grant.DriveGrants.Select(d => d.PermissionedDrive).ToList(),
                    request.Drives.Select(p => p.PermissionedDrive));
                Assert.IsTrue(savedApp.Grant.PermissionSet == request.PermissionSet);

                CollectionAssert.AreEquivalent(savedApp.CircleMemberPermissionSetGrantRequest.Drives.Select(d => d.PermissionedDrive).ToList(),
                    request.CircleMemberPermissionGrant.Drives.Select(p => p.PermissionedDrive).ToList());
                Assert.IsTrue(savedApp.CircleMemberPermissionSetGrantRequest.PermissionSet == request.CircleMemberPermissionGrant.PermissionSet);
            }
        }

        [Test]
        public async Task RevokeAppRegistration()
        {
            var appId = Guid.NewGuid();
            var name = "API Tests Sample App-revoke";
            var corsHostName = "photos.odin.earth";

            var newId = await AddSampleAppNoDrive(appId, name, corsHostName);

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
            {
                var svc = _scaffold.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);
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
            var corsHostName = "app.somewhere.org";
            await AddSampleAppNoDrive(appId, name, corsHostName);

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(_identity.OdinId, out var ownerSharedSecret);
            {
                var svc = _scaffold.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);

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
            var circle1Definition = await _scaffold.OldOwnerApi.CreateCircleWithDrive(_identity.OdinId, "Circle 1", circle1PermissionKeys, circle1Drives);

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(_identity.OdinId, out var ownerSharedSecret);
            {
                var svc = _scaffold.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);
                var request = new AppRegistrationRequest
                {
                    AppId = applicationId,
                    Name = name,
                    Drives = new List<DriveGrantRequest>() { dgr1, dgr2 },
                    PermissionSet = new PermissionSet(new List<int>() { PermissionKeys.ReadCircleMembership, PermissionKeys.ReadConnections }),
                    AuthorizedCircles = new List<Guid>() { circle1Definition.Id },
                    CircleMemberPermissionGrant = new PermissionSetGrantRequest()
                    {
                        Drives = new List<DriveGrantRequest>() { dgr2 },
                        PermissionSet = new PermissionSet(new List<int>() { PermissionKeys.ReadConnections })
                    },
                };

                var response = await svc.RegisterApp(request);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var appReg = response.Content;
                Assert.IsNotNull(appReg);

                var savedApp = await GetSampleApp(applicationId);
                Assert.IsTrue(savedApp.AppId == request.AppId);
                Assert.IsTrue(savedApp.Name == request.Name);

                CollectionAssert.AreEquivalent(savedApp.AuthorizedCircles, request.AuthorizedCircles);

                CollectionAssert.AreEquivalent(savedApp.Grant.DriveGrants.Select(d => d.PermissionedDrive).ToList(),
                    request.Drives.Select(p => p.PermissionedDrive));
                Assert.IsTrue(savedApp.Grant.PermissionSet == request.PermissionSet);

                CollectionAssert.AreEquivalent(savedApp.CircleMemberPermissionSetGrantRequest.Drives.Select(d => d.PermissionedDrive).ToList(),
                    request.CircleMemberPermissionGrant.Drives.Select(p => p.PermissionedDrive).ToList());
                Assert.IsTrue(savedApp.CircleMemberPermissionSetGrantRequest.PermissionSet == request.CircleMemberPermissionGrant.PermissionSet);

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
                Assert.IsTrue(updatedApp.Grant.PermissionSet == updateRequest.PermissionSet);

                // be sure the other fields did not change
                CollectionAssert.AreEquivalent(updatedApp.AuthorizedCircles, request.AuthorizedCircles);
                CollectionAssert.AreEquivalent(updatedApp.CircleMemberPermissionSetGrantRequest.Drives.Select(d => d.PermissionedDrive).ToList(),
                    request.CircleMemberPermissionGrant.Drives.Select(p => p.PermissionedDrive).ToList());
                Assert.IsTrue(updatedApp.CircleMemberPermissionSetGrantRequest.PermissionSet == request.CircleMemberPermissionGrant.PermissionSet);
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
            var circle1Definition = await _scaffold.OldOwnerApi.CreateCircleWithDrive(_identity.OdinId, "Circle 1", circle1PermissionKeys, circle1Drives);

            var circle2PermissionKeys = new List<int>() { PermissionKeys.ReadConnections };
            var circle2Drives = new List<PermissionedDrive>() { new() { Drive = targetDrive1, Permission = DrivePermission.Write } };
            var circle2Definition = await _scaffold.OldOwnerApi.CreateCircleWithDrive(_identity.OdinId, "Circle 2", circle2PermissionKeys, circle2Drives);

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(_identity.OdinId, out var ownerSharedSecret);
            {
                var svc = _scaffold.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);
                var request = new AppRegistrationRequest
                {
                    AppId = applicationId,
                    Name = name,
                    Drives = new List<DriveGrantRequest>() { dgr1, dgr2 },
                    PermissionSet = new PermissionSet(new List<int>() { PermissionKeys.ReadCircleMembership, PermissionKeys.ReadConnections }),
                    AuthorizedCircles = new List<Guid>() { circle1Definition.Id },
                    CircleMemberPermissionGrant = new PermissionSetGrantRequest()
                    {
                        Drives = new List<DriveGrantRequest>() { dgr2 },
                        PermissionSet = new PermissionSet(new List<int>() { PermissionKeys.ReadConnections })
                    }
                };

                var response = await svc.RegisterApp(request);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var appReg = response.Content;
                Assert.IsNotNull(appReg);

                var savedApp = await GetSampleApp(applicationId);
                Assert.IsTrue(savedApp.AppId == request.AppId);
                Assert.IsTrue(savedApp.Name == request.Name);

                CollectionAssert.AreEquivalent(savedApp.AuthorizedCircles, request.AuthorizedCircles);

                CollectionAssert.AreEquivalent(savedApp.Grant.DriveGrants.Select(d => d.PermissionedDrive).ToList(),
                    request.Drives.Select(p => p.PermissionedDrive));
                Assert.IsTrue(savedApp.Grant.PermissionSet == request.PermissionSet);

                CollectionAssert.AreEquivalent(savedApp.CircleMemberPermissionSetGrantRequest.Drives.Select(d => d.PermissionedDrive).ToList(),
                    request.CircleMemberPermissionGrant.Drives.Select(p => p.PermissionedDrive).ToList());
                Assert.IsTrue(savedApp.CircleMemberPermissionSetGrantRequest.PermissionSet == request.CircleMemberPermissionGrant.PermissionSet);

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
                CollectionAssert.AreEquivalent(updatedApp.CircleMemberPermissionSetGrantRequest.Drives.Select(d => d.PermissionedDrive).ToList(),
                    updateRequest.CircleMemberPermissionGrant.Drives.Select(p => p.PermissionedDrive).ToList());

                Assert.IsTrue(updatedApp.CircleMemberPermissionSetGrantRequest.PermissionSet == updateRequest.CircleMemberPermissionGrant.PermissionSet);
                // be sure the other fields did not change

                CollectionAssert.AreEquivalent(updatedApp.Grant.DriveGrants.Select(d => d.PermissionedDrive).ToList(),
                    request.Drives.Select(p => p.PermissionedDrive));
                Assert.IsTrue(updatedApp.Grant.PermissionSet == request.PermissionSet);
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

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var appReg = response.Content;
                Assert.IsNotNull(appReg);

                var savedApp = await GetSampleApp(applicationId);
                Assert.IsTrue(savedApp.AppId == request.AppId);
                Assert.IsTrue(savedApp.Name == request.Name);
                Assert.IsTrue(savedApp.CorsHostName == request.CorsHostName);

                return appReg;
            }
        }

        private async Task<RedactedAppRegistration> GetSampleApp(Guid appId)
        {
            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
            {
                var svc = _scaffold.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);
                var appResponse = await svc.GetRegisteredApp(new GetAppRequest() { AppId = appId });
                Assert.IsTrue(appResponse.IsSuccessStatusCode, $"Could not retrieve the app {appId}");
                Assert.IsNotNull(appResponse.Content, $"Could not retrieve the app {appId}");
                var savedApp = appResponse.Content;
                return savedApp;
            }
        }
    }
}