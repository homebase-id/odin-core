using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests._Universal.Outbox
{
    public class OutboxStatusTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var folder = GetType().Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>() { TestIdentities.Frodo, TestIdentities.Samwise });
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


        public static IEnumerable TestCases()
        {
            yield return new object[] { new GuestWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.MethodNotAllowed };
            yield return new object[] { new AppWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
            yield return new object[] { new OwnerClientContext(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
        }

        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task DriveStatusReportsResult(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
        {
            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

            const DrivePermission drivePermissions = DrivePermission.Write;

            var targetDrive = callerContext.TargetDrive;
            await PrepareScenario(senderOwnerClient, recipientOwnerClient, targetDrive, drivePermissions);

            const string uploadedContent = "pie";

            var fileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = true,
                IsEncrypted = true,
                AppData = new()
                {
                    Content = uploadedContent,
                    FileType = default,
                    GroupId = default,
                    Tags = default
                },
                AccessControlList = AccessControlList.Connected
            };

            var storageOptions = new StorageOptions()
            {
                Drive = targetDrive
            };

            var transitOptions = new TransitOptions()
            {
                Recipients = [recipientOwnerClient.Identity.OdinId],
                RemoteTargetDrive = default
            };

            var (uploadResponse, _) = await senderOwnerClient.DriveRedux.UploadNewEncryptedMetadata(
                fileMetadata,
                storageOptions,
                transitOptions
            );

            ClassicAssert.IsTrue(uploadResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(uploadResponse.StatusCode == HttpStatusCode.OK);
            var uploadResult = uploadResponse.Content;
            ClassicAssert.IsTrue(uploadResult.RecipientStatus.Count == 1);
            ClassicAssert.IsTrue(uploadResult.RecipientStatus[recipientOwnerClient.Identity.OdinId] == TransferStatus.Enqueued);

            // Issue here is that the outbox processes superfast, so we probably need
            // to loaded it up with a bunch of items and not wait on it.  Then we can 
            // check if it has many > 0 items
            await senderOwnerClient.DriveRedux.WaitForEmptyOutbox(targetDrive);
            
            await callerContext.Initialize(senderOwnerClient);
            var driveClient = new UniversalDriveApiClient(senderOwnerClient.Identity.OdinId, callerContext.GetFactory());
            var getStatusResponse = await driveClient.GetDriveStatus(targetDrive);
            ClassicAssert.IsTrue(getStatusResponse.StatusCode == expectedStatusCode);
            if (expectedStatusCode == HttpStatusCode.OK)
            {
                var status = getStatusResponse.Content;
                ClassicAssert.IsTrue(status.Outbox.TotalItems == 0, "Note: review this test since the outbox processing is meant to run in the background");
                ClassicAssert.IsTrue(status.Outbox.CheckedOutCount == 0, "Note: review this test since the outbox processing is meant to run in the background");
            }

            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        private async Task PrepareScenario(OwnerApiClientRedux senderOwnerClient, OwnerApiClientRedux recipientOwnerClient, TargetDrive targetDrive,
            DrivePermission drivePermissions)
        {
            //
            // Recipient creates a target drive
            //
            var recipientDriveResponse = await recipientOwnerClient.DriveManager.CreateDrive(
                targetDrive: targetDrive,
                name: "Target drive on recipient",
                metadata: "",
                allowAnonymousReads: false,
                allowSubscriptions: false,
                ownerOnly: false);

            ClassicAssert.IsTrue(recipientDriveResponse.IsSuccessStatusCode);

            //
            // Sender needs this same drive in order to send across files
            //
            var senderDriveResponse = await senderOwnerClient.DriveManager.CreateDrive(
                targetDrive: targetDrive,
                name: "Target drive on sender",
                metadata: "",
                allowAnonymousReads: false,
                allowSubscriptions: false,
                ownerOnly: false);

            ClassicAssert.IsTrue(senderDriveResponse.IsSuccessStatusCode);

            //
            // Recipient creates a circle with target drive, read and write access
            //
            var expectedPermissionedDrive = new PermissionedDrive()
            {
                Drive = targetDrive,
                Permission = drivePermissions
            };

            var circleId = Guid.NewGuid();
            var createCircleResponse = await recipientOwnerClient.Network.CreateCircle(circleId, "Circle with drive access", new PermissionSetGrantRequest()
            {
                Drives = new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = expectedPermissionedDrive
                    }
                }
            });

            ClassicAssert.IsTrue(createCircleResponse.IsSuccessStatusCode);

            //
            // Sender sends connection request
            //
            await senderOwnerClient.Connections.SendConnectionRequest(recipientOwnerClient.Identity.OdinId, new List<GuidId>());

            //
            // Recipient accepts; grants access to circle
            //
            await recipientOwnerClient.Connections.AcceptConnectionRequest(senderOwnerClient.Identity.OdinId, new List<GuidId>() { circleId });

            // 
            // Test: At this point: recipient should have an ICR record on sender's identity that does not have a key
            // 

            var getConnectionInfoResponse = await recipientOwnerClient.Network.GetConnectionInfo(senderOwnerClient.Identity.OdinId);

            ClassicAssert.IsTrue(getConnectionInfoResponse.IsSuccessStatusCode);
            var senderConnectionInfo = getConnectionInfoResponse.Content;

            ClassicAssert.IsNotNull(senderConnectionInfo.AccessGrant.CircleGrants.SingleOrDefault(cg =>
                cg.DriveGrants.Any(dg => dg.PermissionedDrive == expectedPermissionedDrive)));
        }

        private async Task DeleteScenario(OwnerApiClientRedux senderOwnerClient, OwnerApiClientRedux recipientOwnerClient)
        {
            await _scaffold.OldOwnerApi.DisconnectIdentities(senderOwnerClient.Identity.OdinId, recipientOwnerClient.Identity.OdinId);
        }
    }
}