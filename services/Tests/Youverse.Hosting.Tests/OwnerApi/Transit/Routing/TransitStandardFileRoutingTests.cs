using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives;

namespace Youverse.Hosting.Tests.OwnerApi.Transit.Routing
{
    public class TransitStandardFileRoutingTests
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
        [Ignore("work in progress")]
        public async Task CanTransfer_Unencrypted_StandardFileAndDirectWrite_S1110()
        {
            /*
                Success Test - Standard File
                    Upload standard file - encrypted = false
                    Sender has write access
                    sender has storage key
                    Should succeed
                    Perform direct write (S1110)
                    File is distributed to followers
             */
            
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

            var senderChatCircle = await senderOwnerClient.Network.CreateCircle("Chat Participants", new PermissionSetGrantRequest()
            {
                Drives = new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = expectedPermissionedDrive
                    }
                }
            });

            await senderOwnerClient.Network.SendConnectionRequest(recipient, new List<GuidId>() { senderChatCircle.Id });
            await recipientOwnerClient.Network.AcceptConnectionRequest(sender, new List<GuidId>() { });

            // Test
            // At this point: recipient should have an ICR record on sender's identity that does not have a key
            // 

            var recipientConnectionInfo = await senderOwnerClient.Network.GetConnectionInfo(recipient);

            Assert.IsNotNull(recipientConnectionInfo.AccessGrant.CircleGrants.SingleOrDefault(cg =>
                cg.DriveGrants.Any(dg => dg.PermissionedDrive == senderChatCircle.DriveGrants.Single().PermissionedDrive)));

            await _scaffold.OldOwnerApi.DisconnectIdentities(sender.OdinId, recipient.OdinId);
        }

        [Test]
        public void CanTransfer_Encrypted_StandardFileAndMoveToInbox_S1210_and_S1220()
        {
            Assert.Inconclusive("work in progress");

            /*
                Success Test - Standard File
                    Upload standard file - encrypted = true
                    Sender has write access
                    sender does not have storage key
                    Should succeed
                    File goes to inbox (S1210, S1220)
             */
        }
        
        [Test]
        public void FailsWhenSenderCannotWriteToTargetDriveOnRecipientServer_S1010()
        {
            Assert.Inconclusive("work in progress");
            /*
             Failure Test - Standard
                Fails when sender cannot write to target drive on recipients server
                Upload standard file - encrypted = true
                Sender does not have write access
                sender has  storage key
                Should succeed
                Throws 403
             */
        }
        
        
        
    }
}