using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.OwnerApi.Membership.Connections
{
    public class ExchangeGrantTests
    {
        private WebScaffold _scaffold;

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
        public async Task ExchangeGrantHasNoStorageKey_WhenDrivePermissionRead_IsNot_Granted()
        {
            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            var senderOwnerClient = _scaffold.CreateOwnerApiClient(sender);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClient(recipient);

            var senderChatDrive = await senderOwnerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Chat drive",
                metadata: "",
                allowAnonymousReads: false,
                allowSubscriptions: false,
                ownerOnly: false);

            var expectedPermissionedDrive = new PermissionedDrive()
            {
                Drive = senderChatDrive.TargetDriveInfo,
                Permission = DrivePermission.Write & DrivePermission.WriteReactionsAndComments
            };

            var senderChatCircle = await senderOwnerClient.Membership.CreateCircle("Chat Participants", new PermissionSetGrantRequest()
            {
                Drives = new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = expectedPermissionedDrive
                    }
                }
            });

            await senderOwnerClient.Network.SendConnectionRequestTo(recipient, new List<GuidId>() { senderChatCircle.Id });
            await recipientOwnerClient.Network.AcceptConnectionRequest(sender, new List<GuidId>());

            // Test
            // At this point: recipient should have an ICR record on sender's identity that does not have a key
            // 

            var recipientConnectionInfo = await senderOwnerClient.Network.GetConnectionInfo(recipient);

            //find the drive grant 
            var actualCircleGrant = recipientConnectionInfo.AccessGrant.CircleGrants.SingleOrDefault(cg =>
                cg.DriveGrants.Any(dg => dg.PermissionedDrive == expectedPermissionedDrive));
            Assert.IsNotNull(actualCircleGrant, "actualPermissionedDrive != null");
            Assert.IsTrue(actualCircleGrant.DriveGrants.Count == 1, "There should only be drive grant from the single circle we created");
            Assert.IsFalse(actualCircleGrant.DriveGrants.Single().HasStorageKey, "the drive granted should not have a storage key");

            await _scaffold.OldOwnerApi.DisconnectIdentities(sender.OdinId, recipient.OdinId);
        }
        
        [Test]
        public async Task ExchangeGrantIsGivenStorageKey_WhenDrivePermissionRead_IS_Granted()
        {
            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            var senderOwnerClient = _scaffold.CreateOwnerApiClient(sender);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClient(recipient);

            var senderChatDrive = await senderOwnerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Chat drive",
                metadata: "",
                allowAnonymousReads: false,
                allowSubscriptions: false,
                ownerOnly: false);

            var expectedPermissionedDrive = new PermissionedDrive()
            {
                Drive = senderChatDrive.TargetDriveInfo,
                Permission = DrivePermission.Read | DrivePermission.Write | DrivePermission.WriteReactionsAndComments
            };

            var senderChatCircle = await senderOwnerClient.Membership.CreateCircle("Chat Participants", new PermissionSetGrantRequest()
            {
                Drives = new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = expectedPermissionedDrive
                    }
                }
            });

            await senderOwnerClient.Network.SendConnectionRequestTo(recipient, new List<GuidId>() { senderChatCircle.Id });
            await recipientOwnerClient.Network.AcceptConnectionRequest(sender, new List<GuidId>());

            // Test
            // At this point: recipient should have an ICR record on sender's identity that does not have a key
            // 

            var recipientConnectionInfo = await senderOwnerClient.Network.GetConnectionInfo(recipient);

            //find the drive grant 
            var actualCircleGrant = recipientConnectionInfo.AccessGrant.CircleGrants.SingleOrDefault(cg =>
                cg.DriveGrants.Any(dg => dg.PermissionedDrive == expectedPermissionedDrive));
            Assert.IsNotNull(actualCircleGrant, "actualPermissionedDrive != null");
            Assert.IsTrue(actualCircleGrant.DriveGrants.Count == 1, "There should only be drive grant from the single circle we created");
            Assert.IsTrue(actualCircleGrant.DriveGrants.Single().HasStorageKey, "the drive granted should have storage key");

            await _scaffold.OldOwnerApi.DisconnectIdentities(sender.OdinId, recipient.OdinId);
        }

    }
}