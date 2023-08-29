using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.Management;
using Odin.Core.Services.Membership.Circles;
using Odin.Hosting.Controllers.OwnerToken.Drive;
using Odin.Hosting.Tests.OwnerApi.ApiClient;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Circles;
using Odin.Hosting.Tests.OwnerApi.Drive.Management;
using Odin.Hosting.Tests.OwnerApi.Membership.Circles;

namespace Odin.Hosting.Tests.OwnerApi.Configuration.SystemInit
{
    public class SystemInitializeConfigTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests(initializeIdentity: false);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }

        [Test]
        public async Task CanInitializeSystem_WithAllRsaKeys()
        {
            var identity = TestIdentities.Pippin;
            //success = system drives created, other drives created
            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IOwnerConfigurationClient>(client, ownerSharedSecret);

                var getIsIdentityConfiguredResponse1 = await svc.IsIdentityConfigured();
                Assert.IsTrue(getIsIdentityConfiguredResponse1.IsSuccessStatusCode);
                Assert.IsFalse(getIsIdentityConfiguredResponse1.Content);

                var setupConfig = new InitialSetupRequest()
                {
                    Drives = null,
                    Circles = null
                };

                var initIdentityResponse = await svc.InitializeIdentity(setupConfig);
                Assert.IsTrue(initIdentityResponse.IsSuccessStatusCode);

                var getIsIdentityConfiguredResponse = await svc.IsIdentityConfigured();
                Assert.IsTrue(getIsIdentityConfiguredResponse.IsSuccessStatusCode);
                Assert.IsTrue(getIsIdentityConfiguredResponse.Content);
            }

            var ownerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);

            //
            // Signing Key should exist
            // 
            var signingKey = await ownerClient.PublicPrivateKey.GetSigningPublicKey();
            Assert.IsTrue(signingKey.PublicKey.Length > 0);
            Assert.IsTrue(signingKey.Crc32 > 0);

            //
            // Online key should exist
            //
            var onlinePublicKey = await ownerClient.PublicPrivateKey.GetOnlinePublicKey();
            Assert.IsTrue(onlinePublicKey.PublicKey.Length > 0);
            Assert.IsTrue(onlinePublicKey.Crc32 > 0);

            //
            // Online Ecc key should exist
            var onlineEccPk = await ownerClient.PublicPrivateKey.GetEccOnlinePublicKey();
            Assert.IsTrue(onlineEccPk.PublicKey.Length > 0);
            Assert.IsTrue(onlineEccPk.Crc32 > 0);


            //
            // Online Ecc key should exist
            var offlineEccPk = await ownerClient.PublicPrivateKey.GetEccOfflinePublicKey();
            Assert.IsTrue(offlineEccPk.Length > 0);
            // Assert.IsTrue(offlineEccPk.PublicKey.Length > 0);
            // Assert.IsTrue(offlineEccPk.Crc32 > 0);

            //
            // offline key should exist
            //
            var offlinePublicKey = await ownerClient.PublicPrivateKey.GetOfflinePublicKey();
            Assert.IsTrue(offlinePublicKey.PublicKey.Length > 0);
            Assert.IsTrue(offlinePublicKey.Crc32 > 0);

            CollectionAssert.AreNotEquivalent(signingKey.PublicKey, onlinePublicKey.PublicKey);
            CollectionAssert.AreNotEquivalent(onlinePublicKey.PublicKey, offlinePublicKey.PublicKey);
        }


        [Test]
        public async Task CanInitializeSystem_WithNoAdditionalDrives_and_NoAdditionalCircles()
        {
            var identity = TestIdentities.Samwise;
            //success = system drives created, other drives created
            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IOwnerConfigurationClient>(client, ownerSharedSecret);

                var getIsIdentityConfiguredResponse1 = await svc.IsIdentityConfigured();
                Assert.IsTrue(getIsIdentityConfiguredResponse1.IsSuccessStatusCode);
                Assert.IsFalse(getIsIdentityConfiguredResponse1.Content);

                var setupConfig = new InitialSetupRequest()
                {
                    Drives = null,
                    Circles = null
                };

                var initIdentityResponse = await svc.InitializeIdentity(setupConfig);
                Assert.IsTrue(initIdentityResponse.IsSuccessStatusCode);

                var getIsIdentityConfiguredResponse = await svc.IsIdentityConfigured();
                Assert.IsTrue(getIsIdentityConfiguredResponse.IsSuccessStatusCode);
                Assert.IsTrue(getIsIdentityConfiguredResponse.Content);

                //
                // system drives should be created
                // 
                var driveSvc = RefitCreator.RestServiceFor<IDriveManagementHttpClient>(client, ownerSharedSecret);
                var createdDrivesResponse = await driveSvc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });
                Assert.IsNotNull(createdDrivesResponse.Content);

                var createdDrives = createdDrivesResponse.Content;
                Assert.IsTrue(createdDrives.Results.Count == 6);

                Assert.IsTrue(createdDrives.Results.Any(cd => cd.TargetDriveInfo == SystemDriveConstants.ContactDrive),
                    $"expected drive [{SystemDriveConstants.ContactDrive}] not found");
                Assert.IsTrue(createdDrives.Results.Any(cd => cd.TargetDriveInfo == SystemDriveConstants.ProfileDrive),
                    $"expected drive [{SystemDriveConstants.ProfileDrive}] not found");
                Assert.IsTrue(createdDrives.Results.Any(cd => cd.TargetDriveInfo == SystemDriveConstants.WalletDrive),
                    $"expected drive [{SystemDriveConstants.WalletDrive}] not found");
                Assert.IsTrue(createdDrives.Results.Any(cd => cd.TargetDriveInfo == SystemDriveConstants.ChatDrive),
                    $"expected drive [{SystemDriveConstants.ChatDrive}] not found");
                Assert.IsTrue(createdDrives.Results.Any(cd => cd.TargetDriveInfo == SystemDriveConstants.FeedDrive),
                    $"expected drive [{SystemDriveConstants.FeedDrive}] not found");

                var circleDefinitionService = RefitCreator.RestServiceFor<ICircleDefinitionOwnerClient>(client, ownerSharedSecret);

                var getCircleDefinitionsResponse = await circleDefinitionService.GetCircleDefinitions(includeSystemCircle: true);
                Assert.IsTrue(getCircleDefinitionsResponse.IsSuccessStatusCode);
                Assert.IsNotNull(getCircleDefinitionsResponse.Content);
                var circleDefs = getCircleDefinitionsResponse.Content;
                Assert.IsNotNull(circleDefs);

                Assert.IsTrue(circleDefs.Count() == 1, "Only the system circle should exist");

                var systemCircle = circleDefs.Single();
                Assert.IsTrue(systemCircle.Id == GuidId.FromString("we_are_connected"));
                Assert.IsTrue(systemCircle.Name == "Connected Identities System Circle");

                Assert.IsTrue(systemCircle.Description == "All Connected Identities");
                Assert.IsTrue(systemCircle.DriveGrants.Count() == 3,
                    "By default, there should be two drive grants (standard profile, chat drive, and feed drive)");

                Assert.IsNotNull(systemCircle.DriveGrants.SingleOrDefault(dg =>
                    dg.PermissionedDrive.Drive == SystemDriveConstants.ProfileDrive && dg.PermissionedDrive.Permission == DrivePermission.Read));

                //note: the permission for chat drive is write
                Assert.IsNotNull(systemCircle.DriveGrants.SingleOrDefault(
                    dg => dg.PermissionedDrive.Drive == SystemDriveConstants.ChatDrive && dg.PermissionedDrive.Permission == DrivePermission.Write));
                Assert.IsTrue(!systemCircle.Permissions.Keys.Any(), "By default, the system circle should have no permissions");

                Assert.IsNotNull(systemCircle.DriveGrants.SingleOrDefault(
                    dg => dg.PermissionedDrive.Drive == SystemDriveConstants.FeedDrive && dg.PermissionedDrive.Permission == DrivePermission.Write));
                Assert.IsTrue(!systemCircle.Permissions.Keys.Any(), "By default, the system circle should have no permissions");
            }


            // System apps should be in place
            //
            // var samOwnerClient = _scaffold.CreateOwnerApiClient(identity);
            // var feedAppReg = await samOwnerClient.Apps.GetAppRegistration(SystemAppConstants.FeedAppId);
            // Assert.IsNotNull(feedAppReg, "feed app was not found");
            // Assert.IsFalse(feedAppReg.AuthorizedCircles.Any(), "Feed app should have no authorized circles");
            // Assert.IsTrue(feedAppReg.AppId == SystemAppConstants.FeedAppId.Value);
            // Assert.IsFalse(feedAppReg.Grant.PermissionSet.Keys?.Any() ?? false, "feed app should have no permissions");
            // Assert.IsTrue(feedAppReg.Grant.DriveGrants.All(dg => dg.PermissionedDrive.Drive == SystemDriveConstants.FeedDrive));
            // Assert.IsTrue(feedAppReg.Grant.DriveGrants.All(dg => dg.PermissionedDrive.Permission == DrivePermission.Write), "feed app should be able to write to feed drive");
            // Assert.IsFalse(feedAppReg.Grant.DriveGrants.All(dg => dg.PermissionedDrive.Permission == DrivePermission.Read), "feed app should not be able to read feed drive");
            // Assert.IsFalse(feedAppReg.Grant.DriveGrants.All(dg => dg.PermissionedDrive.Permission == DrivePermission.All), "feed app should not not have all permission to feed drive");
        }

        [Test]
        public async Task CanCreateSystemDrives_With_AdditionalDrivesAndCircles()
        {
            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(TestIdentities.Frodo, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IOwnerConfigurationClient>(client, ownerSharedSecret);

                var contactDrive = SystemDriveConstants.ContactDrive;
                var standardProfileDrive = SystemDriveConstants.ProfileDrive;

                var newDrive = new CreateDriveRequest()
                {
                    Name = "test",
                    AllowAnonymousReads = true,
                    Metadata = "",
                    TargetDrive = TargetDrive.NewTargetDrive()
                };

                var additionalCircleRequest = new CreateCircleRequest()
                {
                    Id = Guid.NewGuid(),
                    Name = "le circle",
                    Description = "an additional circle",
                    DriveGrants = new[]
                    {
                        new DriveGrantRequest()
                        {
                            PermissionedDrive = new PermissionedDrive()
                            {
                                Drive = standardProfileDrive,
                                Permission = DrivePermission.Read
                            }
                        }
                    }
                };

                var setupConfig = new InitialSetupRequest()
                {
                    Drives = new List<CreateDriveRequest>() { newDrive },
                    Circles = new List<CreateCircleRequest>() { additionalCircleRequest }
                };

                var initIdentityResponse = await svc.InitializeIdentity(setupConfig);
                Assert.IsTrue(initIdentityResponse.IsSuccessStatusCode);

                //check if system drives exist
                var expectedDrives = new List<TargetDrive>()
                {
                    standardProfileDrive,
                    contactDrive,
                    SystemDriveConstants.WalletDrive,
                    SystemDriveConstants.ChatDrive,
                    SystemDriveConstants.FeedDrive,
                    SystemDriveConstants.TransientTempDrive,
                    newDrive.TargetDrive
                };

                var driveSvc = RefitCreator.RestServiceFor<IDriveManagementHttpClient>(client, ownerSharedSecret);
                var createdDrivesResponse = await driveSvc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });
                Assert.IsNotNull(createdDrivesResponse.Content);
                var createdDrives = createdDrivesResponse.Content;
                Assert.IsTrue(createdDrives.Results.Count == expectedDrives.Count);

                foreach (var expectedDrive in expectedDrives)
                {
                    Assert.IsTrue(createdDrives.Results.Any(cd => cd.TargetDriveInfo == expectedDrive), $"expected drive [{expectedDrive}] not found");
                }

                var circleDefinitionService = RefitCreator.RestServiceFor<ICircleDefinitionOwnerClient>(client, ownerSharedSecret);

                var getCircleDefinitionsResponse = await circleDefinitionService.GetCircleDefinitions(includeSystemCircle: true);
                Assert.IsTrue(getCircleDefinitionsResponse.IsSuccessStatusCode);
                Assert.IsNotNull(getCircleDefinitionsResponse.Content);
                var circleDefs = getCircleDefinitionsResponse.Content.ToList();

                //
                // System circle exists and has correct grants
                //

                var systemCircle = circleDefs.SingleOrDefault(c => c.Id == CircleConstants.ConnectedIdentitiesSystemCircleId);
                Assert.IsNotNull(systemCircle, "system circle should exist");
                Assert.IsTrue(systemCircle.Id == GuidId.FromString("we_are_connected"));
                Assert.IsTrue(systemCircle.Name == "Connected Identities System Circle");
                Assert.IsTrue(systemCircle.Description == "All Connected Identities");
                Assert.IsTrue(!systemCircle.Permissions.Keys.Any(), "By default, the system circle should have no permissions");

                var newDriveGrant = systemCircle.DriveGrants.SingleOrDefault(dg =>
                    dg.PermissionedDrive.Drive == newDrive.TargetDrive && dg.PermissionedDrive.Permission == DrivePermission.Read);
                Assert.IsNotNull(newDriveGrant, "The new drive should be in the system circle");

                var standardProfileDriveGrant =
                    systemCircle.DriveGrants.SingleOrDefault(dg =>
                        dg.PermissionedDrive.Drive == standardProfileDrive && dg.PermissionedDrive.Permission == DrivePermission.Read);
                Assert.IsNotNull(standardProfileDriveGrant, "The standard profile drive should be in the system circle");

                //note: the permission for chat drive is write
                var chatDriveGrant =
                    systemCircle.DriveGrants.SingleOrDefault(dg =>
                        dg.PermissionedDrive.Drive == SystemDriveConstants.ChatDrive && dg.PermissionedDrive.Permission == DrivePermission.Write);
                Assert.IsNotNull(chatDriveGrant, "the chat drive grant should exist in system circle");


                //
                // additional circle exists
                //
                var additionalCircle = circleDefs.SingleOrDefault(c => c.Id == additionalCircleRequest.Id);
                Assert.IsNotNull(additionalCircle);
                Assert.IsTrue(additionalCircle.Name == "le circle");
                Assert.IsTrue(additionalCircle.Description == "an additional circle");
                Assert.IsTrue(additionalCircle.DriveGrants.Count(dg => dg.PermissionedDrive == additionalCircle.DriveGrants.Single().PermissionedDrive) == 1,
                    "The contact drive should be in the additional circle");
            }
        }
    }
}