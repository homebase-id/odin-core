using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests._Universal.DriveTests.Inbox
{
    // Covers the per-box inbox serialization added to PeerInboxProcessor (box-keyed INodeLock:
    // blocking for explicit/background drains, skip for the inline drain-on-query path).
    //
    // The original bug: a peer "create" (still pending in the recipient's inbox) and a peer "delete"
    // for the same global transit id were drained concurrently; the delete looked up the file before
    // the create committed, failed "not found", and was dropped, leaving the recipient with a file the
    // sender had deleted. These tests assert the recipient converges to Deleted (no orphan).
    //
    // NOTE: these are functional/no-regression and concurrency-exercise tests, not a *deterministic*
    // reproduction of the race. Reliably losing the race pre-fix needs a timing seam (make the create's
    // commit block until the delete is mid-flight), which the public API does not expose. The
    // create-and-delete-both-pending cases below are the closest deterministic analog: FIFO-within-box
    // plus single-drainer serialization must process create-before-delete and end at Deleted.

    public class PeerInboxDeleteSerializationTests
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

        [Test]
        public async Task DistributedFile_DrainsToRecipient_AsActive()
        {
            // No-regression: the blocking, box-locked drain still processes a normal peer create.
            var sender = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
            var recipient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

            var targetDrive = TargetDrive.NewTargetDrive();
            await PrepareScenario(sender, recipient, targetDrive, DrivePermission.Write);

            var uploadResult = await UploadDistributedFile(sender, targetDrive, "hello mordor", TestIdentities.Samwise);
            await sender.DriveRedux.WaitForEmptyOutbox(targetDrive);

            var processResponse = await recipient.DriveRedux.ProcessInbox(targetDrive, batchSize: 100);
            ClassicAssert.IsTrue(processResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(processResponse.Content.TotalItems == 0, "recipient inbox should be drained");

            await AssertRecipientFileState(recipient, uploadResult, FileState.Active);

            await DeleteScenario(sender, recipient);
        }

        [Test]
        public async Task Delete_AfterCreateProcessed_SoftDeletesOnRecipient()
        {
            // Create is drained first, then a separate delete is distributed and drained.
            var sender = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
            var recipient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

            var targetDrive = TargetDrive.NewTargetDrive();
            await PrepareScenario(sender, recipient, targetDrive, DrivePermission.Write);

            var uploadResult = await UploadDistributedFile(sender, targetDrive, "delete me later", TestIdentities.Samwise);
            await sender.DriveRedux.WaitForEmptyOutbox(targetDrive);

            await recipient.DriveRedux.ProcessInbox(targetDrive, batchSize: 100);
            await AssertRecipientFileState(recipient, uploadResult, FileState.Active);

            // Sender deletes its file and propagates the delete to the recipient.
            var deleteResponse = await sender.DriveRedux.SoftDeleteFile(
                uploadResult.File,
                recipients: new List<string> { TestIdentities.Samwise.OdinId.DomainName });
            ClassicAssert.IsTrue(deleteResponse.IsSuccessStatusCode);
            await sender.DriveRedux.WaitForEmptyOutbox(targetDrive);

            var processResponse = await recipient.DriveRedux.ProcessInbox(targetDrive, batchSize: 100);
            ClassicAssert.IsTrue(processResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(processResponse.Content.TotalItems == 0, "recipient inbox should be drained");

            await AssertRecipientFileState(recipient, uploadResult, FileState.Deleted);

            await DeleteScenario(sender, recipient);
        }

        [Test]
        public async Task CreateAndDeleteBothPending_DrainResultsInDeleted_NotOrphaned()
        {
            // The bug's shape: both the create and the delete are sitting in the recipient's inbox
            // before any drain runs. FIFO-within-box + single-drainer serialization must process the
            // create first and then the delete, ending at Deleted rather than orphaning the file.
            var sender = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
            var recipient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

            var targetDrive = TargetDrive.NewTargetDrive();
            await PrepareScenario(sender, recipient, targetDrive, DrivePermission.Write);

            // Distribute the create, then the delete, WITHOUT draining the recipient in between, so
            // both land in the recipient's inbox as pending items.
            var uploadResult = await UploadDistributedFile(sender, targetDrive, "transient file", TestIdentities.Samwise);
            await sender.DriveRedux.WaitForEmptyOutbox(targetDrive);

            var deleteResponse = await sender.DriveRedux.SoftDeleteFile(
                uploadResult.File,
                recipients: new List<string> { TestIdentities.Samwise.OdinId.DomainName });
            ClassicAssert.IsTrue(deleteResponse.IsSuccessStatusCode);
            await sender.DriveRedux.WaitForEmptyOutbox(targetDrive);

            // Single drain processes both pending items in order.
            var processResponse = await recipient.DriveRedux.ProcessInbox(targetDrive, batchSize: 100);
            ClassicAssert.IsTrue(processResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(processResponse.Content.TotalItems == 0, "recipient inbox should be fully drained");

            await AssertRecipientFileState(recipient, uploadResult, FileState.Deleted);

            await DeleteScenario(sender, recipient);
        }

        [Test]
        public async Task ConcurrentDrains_WithCreateAndDeletePending_ConvergeToDeleted()
        {
            // Exercises the box lock under concurrency: several drains race on the same box while a
            // create and a delete are both pending. The lock serializes them, so the box drains fully
            // and converges to Deleted with no error and no orphan.
            var sender = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
            var recipient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

            var targetDrive = TargetDrive.NewTargetDrive();
            await PrepareScenario(sender, recipient, targetDrive, DrivePermission.Write);

            var uploadResult = await UploadDistributedFile(sender, targetDrive, "race me", TestIdentities.Samwise);
            await sender.DriveRedux.WaitForEmptyOutbox(targetDrive);

            var deleteResponse = await sender.DriveRedux.SoftDeleteFile(
                uploadResult.File,
                recipients: new List<string> { TestIdentities.Samwise.OdinId.DomainName });
            ClassicAssert.IsTrue(deleteResponse.IsSuccessStatusCode);
            await sender.DriveRedux.WaitForEmptyOutbox(targetDrive);

            // Fire several concurrent drains of the same box.
            var drainTasks = Enumerable.Range(0, 4)
                .Select(_ => recipient.DriveRedux.ProcessInbox(targetDrive, batchSize: 100))
                .ToList();
            var drainResults = await Task.WhenAll(drainTasks);
            foreach (var r in drainResults)
            {
                ClassicAssert.IsTrue(r.IsSuccessStatusCode, "concurrent drain should not error");
            }

            // A final settling drain leaves the inbox empty.
            var settle = await recipient.DriveRedux.ProcessInbox(targetDrive, batchSize: 100);
            ClassicAssert.IsTrue(settle.IsSuccessStatusCode);
            ClassicAssert.IsTrue(settle.Content.TotalItems == 0, "recipient inbox should be fully drained");

            await AssertRecipientFileState(recipient, uploadResult, FileState.Deleted);

            await DeleteScenario(sender, recipient);
        }

        private async Task<UploadResult> UploadDistributedFile(OwnerApiClientRedux sender, TargetDrive targetDrive, string content,
            TestIdentity recipient)
        {
            var fileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = true,
                IsEncrypted = false,
                AppData = new()
                {
                    Content = content
                },
                AccessControlList = AccessControlList.Connected
            };

            var transitOptions = new TransitOptions()
            {
                Recipients = [recipient.OdinId]
            };

            var response = await sender.DriveRedux.UploadNewMetadata(targetDrive, fileMetadata, transitOptions);
            ClassicAssert.IsTrue(response.IsSuccessStatusCode);

            var uploadResult = response.Content;
            ClassicAssert.IsTrue(uploadResult.RecipientStatus.TryGetValue(recipient.OdinId, out var status));
            ClassicAssert.IsTrue(status == TransferStatus.Enqueued, $"unexpected transfer status {status}");

            return uploadResult;
        }

        private static async Task AssertRecipientFileState(OwnerApiClientRedux recipient, UploadResult uploadResult, FileState expected)
        {
            var response = await recipient.DriveRedux.QueryByGlobalTransitId(uploadResult.GlobalTransitIdFileIdentifier);
            ClassicAssert.IsTrue(response.IsSuccessStatusCode);

            var file = response.Content?.SearchResults?.SingleOrDefault();
            ClassicAssert.IsNotNull(file, "recipient should have the file by global transit id");
            ClassicAssert.IsTrue(file.FileState == expected, $"expected {expected} but was {file.FileState}");
        }

        private async Task PrepareScenario(OwnerApiClientRedux senderOwnerClient, OwnerApiClientRedux recipientOwnerClient,
            TargetDrive targetDrive, DrivePermission drivePermissions)
        {
            // Recipient creates the target drive.
            var recipientDriveResponse = await recipientOwnerClient.DriveManager.CreateDrive(
                targetDrive: targetDrive,
                name: "Target drive on recipient",
                metadata: "",
                allowAnonymousReads: false,
                allowSubscriptions: false,
                ownerOnly: false);
            ClassicAssert.IsTrue(recipientDriveResponse.IsSuccessStatusCode);

            // Sender needs the same drive to send files across.
            var senderDriveResponse = await senderOwnerClient.DriveManager.CreateDrive(
                targetDrive: targetDrive,
                name: "Target drive on sender",
                metadata: "",
                allowAnonymousReads: false,
                allowSubscriptions: false,
                ownerOnly: false);
            ClassicAssert.IsTrue(senderDriveResponse.IsSuccessStatusCode);

            // Recipient creates a circle granting access to the target drive.
            var expectedPermissionedDrive = new PermissionedDrive()
            {
                Drive = targetDrive,
                Permission = drivePermissions
            };

            var circleId = Guid.NewGuid();
            var createCircleResponse = await recipientOwnerClient.Network.CreateCircle(circleId, "Circle with drive access",
                new PermissionSetGrantRequest()
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

            // Connect the two identities, granting the sender the circle.
            await senderOwnerClient.Connections.SendConnectionRequest(recipientOwnerClient.Identity.OdinId, new List<GuidId>() { });
            await recipientOwnerClient.Connections.AcceptConnectionRequest(senderOwnerClient.Identity.OdinId,
                new List<GuidId>() { circleId });
        }

        private async Task DeleteScenario(OwnerApiClientRedux senderOwnerClient, OwnerApiClientRedux recipientOwnerClient)
        {
            await _scaffold.OldOwnerApi.DisconnectIdentities(senderOwnerClient.Identity.OdinId, recipientOwnerClient.Identity.OdinId);
        }
    }
}
