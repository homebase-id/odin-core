using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.Circles;

namespace Odin.Hosting.Tests.OwnerApi.Configuration.SystemInit
{
    public class SystemInitializeConfigTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var folder = GetType().Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests(initializeIdentity: false, testIdentities: new List<TestIdentity>() { TestIdentities.Frodo });
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
        public async Task CanInitializeSystem_WithAllRsaKeys()
        {
            var ownerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);

            //success = system drives created, other drives created
            var getIsIdentityConfiguredResponse1 = await ownerClient.Configuration.IsIdentityConfigured();
            ClassicAssert.IsTrue(getIsIdentityConfiguredResponse1.IsSuccessStatusCode);
            ClassicAssert.IsFalse(getIsIdentityConfiguredResponse1.Content);

            var setupConfig = new InitialSetupRequest()
            {
                Drives = null,
                Circles = null
            };

            var initIdentityResponse = await ownerClient.Configuration.InitializeIdentity(setupConfig);
            ClassicAssert.IsTrue(initIdentityResponse.IsSuccessStatusCode);

            var getIsIdentityConfiguredResponse = await ownerClient.Configuration.IsIdentityConfigured();
            ClassicAssert.IsTrue(getIsIdentityConfiguredResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(getIsIdentityConfiguredResponse.Content);


            //
            // Signing Key should exist
            //
            var signingKey = await ownerClient.PublicPrivateKey.GetSigningPublicKey();
            ClassicAssert.IsTrue(signingKey.PublicKey.Length > 0);
            ClassicAssert.IsTrue(signingKey.Crc32 > 0);

            //
            // Online key should exist
            //
            var onlinePublicKey = await ownerClient.PublicPrivateKey.GetOnlinePublicKey();
            ClassicAssert.IsTrue(onlinePublicKey.PublicKey.Length > 0);
            ClassicAssert.IsTrue(onlinePublicKey.Crc32 > 0);

            //
            // Online Ecc key should exist
            var onlineEccPk = await ownerClient.PublicPrivateKey.GetEccOnlinePublicKey();
            ClassicAssert.IsTrue(onlineEccPk.PublicKeyJwkBase64Url.Length > 0);
            ClassicAssert.IsTrue(onlineEccPk.CRC32c > 0);


            //
            // Online Ecc key should exist
            var offlineEccPk = await ownerClient.PublicPrivateKey.GetEccOfflinePublicKey();
            ClassicAssert.IsTrue(offlineEccPk.Length > 0);
            // ClassicAssert.IsTrue(offlineEccPk.PublicKey.Length > 0);
            // ClassicAssert.IsTrue(offlineEccPk.Crc32 > 0);

            //
            // offline key should exist
            //
            var offlinePublicKey = await ownerClient.PublicPrivateKey.GetOfflinePublicKey();
            ClassicAssert.IsTrue(offlinePublicKey.PublicKey.Length > 0);
            ClassicAssert.IsTrue(offlinePublicKey.Crc32 > 0);

            CollectionAssert.AreNotEquivalent(signingKey.PublicKey, onlinePublicKey.PublicKey);
            CollectionAssert.AreNotEquivalent(onlinePublicKey.PublicKey, offlinePublicKey.PublicKey);
        }

        [Test]
        public async Task CanInitializeSystem_WithNoAdditionalDrives_and_NoAdditionalCircles()
        {
            var ownerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);

            //success = system drives created, other drives created

            var getIsIdentityConfiguredResponse1 = await ownerClient.Configuration.IsIdentityConfigured();
            ClassicAssert.IsTrue(getIsIdentityConfiguredResponse1.IsSuccessStatusCode);
            ClassicAssert.IsFalse(getIsIdentityConfiguredResponse1.Content);

            var setupConfig = new InitialSetupRequest()
            {
                Drives = null,
                Circles = null
            };

            var initIdentityResponse = await ownerClient.Configuration.InitializeIdentity(setupConfig);
            ClassicAssert.IsTrue(initIdentityResponse.IsSuccessStatusCode);

            var getIsIdentityConfiguredResponse = await ownerClient.Configuration.IsIdentityConfigured();
            ClassicAssert.IsTrue(getIsIdentityConfiguredResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(getIsIdentityConfiguredResponse.Content);

            //
            // system drives should be created
            //
            var createdDrivesResponse = await ownerClient.Drive.GetDrives(1, 100);
            ClassicAssert.IsNotNull(createdDrivesResponse.Content);

            var createdDrives = createdDrivesResponse.Content;
            ClassicAssert.IsTrue(createdDrives.Results.Count == SystemDriveConstants.SystemDrives.Count);

            ClassicAssert.IsTrue(createdDrives.Results.Any(cd => cd.TargetDriveInfo == SystemDriveConstants.ContactDrive),
                $"expected drive [{SystemDriveConstants.ContactDrive}] not found");
            ClassicAssert.IsTrue(createdDrives.Results.Any(cd => cd.TargetDriveInfo == SystemDriveConstants.ProfileDrive),
                $"expected drive [{SystemDriveConstants.ProfileDrive}] not found");
            ClassicAssert.IsTrue(createdDrives.Results.Any(cd => cd.TargetDriveInfo == SystemDriveConstants.WalletDrive),
                $"expected drive [{SystemDriveConstants.WalletDrive}] not found");
            ClassicAssert.IsTrue(createdDrives.Results.Any(cd => cd.TargetDriveInfo == SystemDriveConstants.ChatDrive),
                $"expected drive [{SystemDriveConstants.ChatDrive}] not found");
            ClassicAssert.IsTrue(createdDrives.Results.Any(cd => cd.TargetDriveInfo == SystemDriveConstants.MailDrive),
                $"expected drive [{SystemDriveConstants.MailDrive}] not found");
            ClassicAssert.IsTrue(createdDrives.Results.Any(cd => cd.TargetDriveInfo == SystemDriveConstants.FeedDrive),
                $"expected drive [{SystemDriveConstants.FeedDrive}] not found");

            ClassicAssert.IsTrue(createdDrives.Results.Any(cd => cd.TargetDriveInfo == SystemDriveConstants.HomePageConfigDrive),
                $"expected drive [{SystemDriveConstants.HomePageConfigDrive}] not found");

            ClassicAssert.IsTrue(createdDrives.Results.Any(cd => cd.TargetDriveInfo == SystemDriveConstants.PublicPostsChannelDrive),
                $"expected drive [{SystemDriveConstants.PublicPostsChannelDrive}] not found");


            var getCircleDefinitionsResponse = await ownerClient.Membership.GetCircleDefinitions(includeSystemCircle: true);
            ClassicAssert.IsTrue(getCircleDefinitionsResponse.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(getCircleDefinitionsResponse.Content);
            var circleDefs = getCircleDefinitionsResponse.Content?.ToList();
            ClassicAssert.IsNotNull(circleDefs);

            ClassicAssert.IsTrue(circleDefs.Count() == SystemCircleConstants.AllSystemCircles.Count, "not all system circles were created");

            var connectedIdentitiesSystemCircle = circleDefs.Single(c => c.Id == SystemCircleConstants.ConfirmedConnectionsCircleId);
            ClassicAssert.IsTrue(connectedIdentitiesSystemCircle.Id == GuidId.FromString("we_are_connected"));
            ClassicAssert.IsTrue(connectedIdentitiesSystemCircle.DriveGrants.Count() == 7);

            ClassicAssert.IsNotNull(connectedIdentitiesSystemCircle.DriveGrants.SingleOrDefault(dg =>
                dg.PermissionedDrive.Drive == SystemDriveConstants.ProfileDrive && dg.PermissionedDrive.Permission == DrivePermission.Read));

            ClassicAssert.IsNotNull(connectedIdentitiesSystemCircle.DriveGrants.SingleOrDefault(
                dg => dg.PermissionedDrive.Drive == SystemDriveConstants.ChatDrive &&
                      dg.PermissionedDrive.Permission.HasFlag(DrivePermission.Write | DrivePermission.React)));
            ClassicAssert.IsTrue(connectedIdentitiesSystemCircle.Permissions.Keys.Count == 1, "By default, the system circle should have 1 permission");
            ClassicAssert.IsNotNull(connectedIdentitiesSystemCircle.Permissions.Keys.SingleOrDefault(k => k == PermissionKeys.AllowIntroductions));

            ClassicAssert.IsNotNull(connectedIdentitiesSystemCircle.DriveGrants.SingleOrDefault(
                dg => dg.PermissionedDrive.Drive == SystemDriveConstants.MailDrive &&
                      dg.PermissionedDrive.Permission.HasFlag(DrivePermission.Write | DrivePermission.React)));

            ClassicAssert.IsNotNull(connectedIdentitiesSystemCircle.DriveGrants.SingleOrDefault(
                dg => dg.PermissionedDrive.Drive == SystemDriveConstants.FeedDrive &&
                      dg.PermissionedDrive.Permission.HasFlag(DrivePermission.Write | DrivePermission.React)));

            //

            var autoConnectionsSystemCircle = circleDefs.Single(c => c.Id == SystemCircleConstants.AutoConnectionsCircleId);
            ClassicAssert.IsTrue(autoConnectionsSystemCircle.Name == "Auto-connected Identities");

            // ClassicAssert.IsNotNull(autoConnectionsSystemCircle.DriveGrants.SingleOrDefault(dg =>
            //     dg.PermissionedDrive.Drive == SystemDriveConstants.ProfileDrive && dg.PermissionedDrive.Permission == DrivePermission.Read));

            ClassicAssert.IsNotNull(autoConnectionsSystemCircle.DriveGrants.SingleOrDefault(
                dg => dg.PermissionedDrive.Drive == SystemDriveConstants.ChatDrive &&
                      dg.PermissionedDrive.Permission.HasFlag(DrivePermission.Write | DrivePermission.React)));

            ClassicAssert.IsTrue(!autoConnectionsSystemCircle.Permissions.Keys.Exists(k => k == PermissionKeys.AllowIntroductions));

            ClassicAssert.IsNotNull(autoConnectionsSystemCircle.DriveGrants.SingleOrDefault(
                dg => dg.PermissionedDrive.Drive == SystemDriveConstants.MailDrive &&
                      dg.PermissionedDrive.Permission.HasFlag(DrivePermission.Write | DrivePermission.React)));

            ClassicAssert.IsNotNull(autoConnectionsSystemCircle.DriveGrants.SingleOrDefault(
                dg => dg.PermissionedDrive.Drive == SystemDriveConstants.FeedDrive &&
                      dg.PermissionedDrive.Permission.HasFlag(DrivePermission.Write | DrivePermission.React)));


            // Granted via allowAnonymous read
            ClassicAssert.IsNotNull(autoConnectionsSystemCircle.DriveGrants.SingleOrDefault(
                dg => dg.PermissionedDrive.Drive == SystemDriveConstants.ProfileDrive &&
                      dg.PermissionedDrive.Permission.HasFlag(DrivePermission.Read)));


            // Granted via allowAnonymous read
            ClassicAssert.IsNotNull(autoConnectionsSystemCircle.DriveGrants.SingleOrDefault(
                dg => dg.PermissionedDrive.Drive == SystemDriveConstants.HomePageConfigDrive &&
                      dg.PermissionedDrive.Permission.HasFlag(DrivePermission.Read)));


            // Granted via allowAnonymous read
            ClassicAssert.IsNotNull(autoConnectionsSystemCircle.DriveGrants.SingleOrDefault(
                dg => dg.PermissionedDrive.Drive == SystemDriveConstants.PublicPostsChannelDrive &&
                      dg.PermissionedDrive.Permission.HasFlag(DrivePermission.Read)));


            // System apps should be in place
            //
            // var samOwnerClient = _scaffold.CreateOwnerApiClient(identity);
            // var feedAppReg = await samOwnerClient.Apps.GetAppRegistration(SystemAppConstants.FeedAppId);
            // ClassicAssert.IsNotNull(feedAppReg, "feed app was not found");
            // ClassicAssert.IsFalse(feedAppReg.AuthorizedCircles.Any(), "Feed app should have no authorized circles");
            // ClassicAssert.IsTrue(feedAppReg.AppId == SystemAppConstants.FeedAppId.Value);
            // ClassicAssert.IsFalse(feedAppReg.Grant.PermissionSet.Keys?.Any() ?? false, "feed app should have no permissions");
            // ClassicAssert.IsTrue(feedAppReg.Grant.DriveGrants.All(dg => dg.PermissionedDrive.Drive == SystemDriveConstants.FeedDrive));
            // ClassicAssert.IsTrue(feedAppReg.Grant.DriveGrants.All(dg => dg.PermissionedDrive.Permission == DrivePermission.Write), "feed app should be able to write to feed drive");
            // ClassicAssert.IsFalse(feedAppReg.Grant.DriveGrants.All(dg => dg.PermissionedDrive.Permission == DrivePermission.Read), "feed app should not be able to read feed drive");
            // ClassicAssert.IsFalse(feedAppReg.Grant.DriveGrants.All(dg => dg.PermissionedDrive.Permission == DrivePermission.All), "feed app should not not have all permission to feed drive");
        }

        [Test]
        public async Task CanCreateSystemDrives_With_AdditionalDrivesAndCircles()
        {
            var ownerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);

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

            var initIdentityResponse = await ownerClient.Configuration.InitializeIdentity(setupConfig);
            ClassicAssert.IsTrue(initIdentityResponse.IsSuccessStatusCode);

            //check if system drives exist
            var expectedDrives = SystemDriveConstants.SystemDrives.Concat([newDrive.TargetDrive]);

            var createdDrivesResponse = await ownerClient.Drive.GetDrives(1, 100);
            ClassicAssert.IsNotNull(createdDrivesResponse.Content);
            var createdDrives = createdDrivesResponse.Content;
            ClassicAssert.IsTrue(createdDrives.Results.Count == expectedDrives.Count());

            foreach (var expectedDrive in expectedDrives)
            {
                ClassicAssert.IsTrue(createdDrives.Results.Any(cd => cd.TargetDriveInfo == expectedDrive), $"expected drive [{expectedDrive}] not found");
            }

            var getCircleDefinitionsResponse = await ownerClient.Membership.GetCircleDefinitions(includeSystemCircle: true);
            ClassicAssert.IsTrue(getCircleDefinitionsResponse.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(getCircleDefinitionsResponse.Content);
            var circleDefs = getCircleDefinitionsResponse.Content.ToList();

            //
            // System circle exists and has correct grants
            //

            var systemCircle = circleDefs.SingleOrDefault(c => c.Id == SystemCircleConstants.ConfirmedConnectionsCircleId);
            ClassicAssert.IsNotNull(systemCircle, "system circle should exist");
            ClassicAssert.IsTrue(systemCircle.Id == GuidId.FromString("we_are_connected"));
            ClassicAssert.IsTrue(systemCircle.Name == "Confirmed Connected Identities");
            ClassicAssert.IsTrue(systemCircle.Description ==
                          "Contains identities which you have confirmed as a connection, either by approving the connection yourself or upgrading an introduced connection");
            ClassicAssert.IsTrue(systemCircle.Permissions.Keys.Count == 1, "By default, the system circle should have 1 permission");
            ClassicAssert.IsNotNull(systemCircle.Permissions.Keys.SingleOrDefault(k => k == PermissionKeys.AllowIntroductions));

            var newDriveGrant = systemCircle.DriveGrants.SingleOrDefault(dg =>
                dg.PermissionedDrive.Drive == newDrive.TargetDrive && dg.PermissionedDrive.Permission == DrivePermission.Read);
            ClassicAssert.IsNotNull(newDriveGrant, "The new drive should be in the system circle");

            var standardProfileDriveGrant =
                systemCircle.DriveGrants.SingleOrDefault(dg =>
                    dg.PermissionedDrive.Drive == standardProfileDrive && dg.PermissionedDrive.Permission == DrivePermission.Read);
            ClassicAssert.IsNotNull(standardProfileDriveGrant, "The standard profile drive should be in the system circle");

            //note: the permission for chat drive is write
            var chatDriveGrant =
                systemCircle.DriveGrants.SingleOrDefault(dg =>
                    dg.PermissionedDrive.Drive == SystemDriveConstants.ChatDrive &&
                    dg.PermissionedDrive.Permission.HasFlag(DrivePermission.Write | DrivePermission.React));
            ClassicAssert.IsNotNull(chatDriveGrant, "the chat drive grant should exist in system circle");


            //
            // additional circle exists
            //
            var additionalCircle = circleDefs.SingleOrDefault(c => c.Id == additionalCircleRequest.Id);
            ClassicAssert.IsNotNull(additionalCircle);
            ClassicAssert.IsTrue(additionalCircle.Name == "le circle");
            ClassicAssert.IsTrue(additionalCircle.Description == "an additional circle");
            ClassicAssert.IsTrue(additionalCircle.DriveGrants.Count(dg => dg.PermissionedDrive == additionalCircle.DriveGrants.Single().PermissionedDrive) == 1,
                "The contact drive should be in the additional circle");
        }

        // [Test]
        // public async Task WillAutoFollow_IdHomebaseId()
        // {
        //     var ownerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        //
        //     var setupConfig = new InitialSetupRequest()
        //     {
        //         Drives = [],
        //         Circles = []
        //     };
        //
        //     var initIdentityResponse = await ownerClient.Configuration.InitializeIdentity(setupConfig);
        //     ClassicAssert.IsTrue(initIdentityResponse.IsSuccessStatusCode);
        //
        //     var followerDefinition = await ownerClient.OwnerFollower.GetIdentityIFollow(TestIdentities.HomebaseId);
        //
        //     ClassicAssert.IsNotNull(followerDefinition);
        //     ClassicAssert.IsTrue(followerDefinition.OdinId == TestIdentities.HomebaseId.OdinId);
        //     ClassicAssert.IsTrue(followerDefinition.NotificationType == FollowerNotificationType.AllNotifications);
        // }
    }
}