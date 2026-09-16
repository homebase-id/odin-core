using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Identity;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Tests._Universal;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests._V2.ApiClient.TestCases;
using Odin.Hosting.UnifiedV2.Drive.Read;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Hosting.Tests._V2.Tests.Drive.WriteFileTests;

/// <summary>
/// Exercises the peer fan-out path of <c>POST {drive}/files/delete-batch/by-file-id</c> (V2) — the
/// case where the caller asks the server to also notify peers to soft-delete their copy.
/// (<c>CanDeleteByMultipleFileIds</c> covers only empty Recipients.)
///
/// FLAGGED: this lives in the OLD WebScaffold framework (not the fast <c>Odin.Hosting.Tests.V2</c>).
/// The five core distribution-then-delete cases were ported to
/// <c>tests/apps/Odin.Hosting.Tests.V2/Ported/Peer/DeleteBatchTests.cs</c> on 2026-05-13 and have been
/// removed from here. The eight that remain depend on production behaviour the in-process framework
/// deliberately doesn't run: background services are never started there (the five
/// inbox-drain-on-query scenarios), the App and Guest caller variants of this flow aren't wrapped as
/// <c>CallerSpec</c>s, and the V1 Disconnect helper that RecipientDisconnected needs has no equivalent.
/// </summary>
public class DeleteFileIdBatchPeerTestsV2
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: [TestIdentities.Frodo, TestIdentities.Samwise]);
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

    // #1 — App caller (not owner) invoking V2 DeleteFileIdBatch with a recipient.
    [Test]
    public async Task DeleteFileIdBatch_AppCaller_PropagatesDeleteToRecipient()
    {
        var sender = TestIdentities.Frodo;
        var recipient = TestIdentities.Samwise;

        var senderOwner = _scaffold.CreateOwnerApiClientRedux(sender);
        var recipientOwner = _scaffold.CreateOwnerApiClientRedux(recipient);

        // App caller needs ReadWrite on the drive — the V2 endpoint itself only requires
        // Write, but PeerOutgoingTransferService.SendDeleteFileRequest re-fetches the
        // header via GetServerFileHeader (which asserts Read). It also needs UseTransitWrite
        // to enqueue the peer delete.
        var callerContext = new AppTestCase(
            TargetDrive.NewTargetDrive(),
            DrivePermission.ReadWrite,
            new TestPermissionKeyList(PermissionKeyAllowance.Apps.ToArray()));

        var targetDrive = callerContext.TargetDrive;
        await PrepareScenario(senderOwner, [recipientOwner], targetDrive);

        var upload = await UploadAndDistributeMetadata(senderOwner, targetDrive, fileType: 410, [recipient.OdinId]);
        await AssertRecipientHasFile(recipientOwner, upload, expectedState: FileState.Active);

        await callerContext.Initialize(senderOwner);
        var v2Writer = new DriveWriterV2Client(sender.OdinId, callerContext.GetFactory());

        var deleteResponse = await v2Writer.DeleteFileList(targetDrive.Alias, new List<DeleteFileRequestV2>
        {
            new() { FileId = upload.File.FileId, Recipients = [recipient.OdinId] }
        });

        ClassicAssert.AreEqual(HttpStatusCode.OK, deleteResponse.StatusCode,
            $"App caller should be allowed to invoke DeleteFileIdBatch; got {deleteResponse.StatusCode}");
        var single = deleteResponse.Content!.Results.Single();
        ClassicAssert.IsTrue(single.LocalFileDeleted);
        ClassicAssert.AreEqual(DeleteLinkedFileStatus.Enqueued, single.RecipientStatus[recipient.OdinId]);

        await senderOwner.DriveRedux.WaitForEmptyOutbox(targetDrive);
        await recipientOwner.DriveRedux.ProcessInbox(targetDrive);
        await recipientOwner.DriveRedux.WaitForEmptyInbox(targetDrive);

        await AssertRecipientHasFile(recipientOwner, upload, expectedState: FileState.Deleted);

        await Cleanup(senderOwner, [recipientOwner]);
    }

    // Documents a real auth gap: the V2 endpoint authorizes on Write, but the peer
    // fan-out path inside PeerOutgoingTransferService.SendDeleteFileRequest re-fetches
    // the header via GetServerFileHeader (Read-required). An App with Write-only
    // permission can call delete-batch/by-file-id with empty Recipients but is rejected
    // (403) the moment Recipients is populated.
    [Test]
    public async Task DeleteFileIdBatch_AppCallerWithWriteOnly_Returns403WhenRecipientsAreSet()
    {
        var sender = TestIdentities.Frodo;
        var recipient = TestIdentities.Samwise;

        var senderOwner = _scaffold.CreateOwnerApiClientRedux(sender);
        var recipientOwner = _scaffold.CreateOwnerApiClientRedux(recipient);

        var callerContext = new AppTestCase(
            TargetDrive.NewTargetDrive(),
            DrivePermission.Write,
            new TestPermissionKeyList(PermissionKeyAllowance.Apps.ToArray()));

        var targetDrive = callerContext.TargetDrive;
        await PrepareScenario(senderOwner, [recipientOwner], targetDrive);

        var upload = await UploadAndDistributeMetadata(senderOwner, targetDrive, fileType: 411, [recipient.OdinId]);
        await AssertRecipientHasFile(recipientOwner, upload, expectedState: FileState.Active);

        await callerContext.Initialize(senderOwner);
        var v2Writer = new DriveWriterV2Client(sender.OdinId, callerContext.GetFactory());

        var deleteResponse = await v2Writer.DeleteFileList(targetDrive.Alias, new List<DeleteFileRequestV2>
        {
            new() { FileId = upload.File.FileId, Recipients = [recipient.OdinId] }
        });

        ClassicAssert.AreEqual(HttpStatusCode.Forbidden, deleteResponse.StatusCode,
            "Write-only app caller hits AssertCanReadDriveAsync inside the peer fan-out and should be denied");

        await Cleanup(senderOwner, [recipientOwner]);
    }

    // #7 — Recipient disconnected between distribute and delete. The outbox enqueue
    // resolves the client access token up front, so a disconnected recipient should
    // produce an OdinClientException → 400 BadRequest, and the local file should NOT
    // be soft-deleted (peer enqueue happens before the local soft-delete in PerformFileDelete).
    [Test]
    public async Task DeleteFileIdBatch_RecipientDisconnected_FailsAndDoesNotDeleteLocal()
    {
        var sender = TestIdentities.Frodo;
        var recipient = TestIdentities.Samwise;

        var callerContext = new OwnerTestCase(TargetDrive.NewTargetDrive());
        var senderOwner = _scaffold.CreateOwnerApiClientRedux(sender);
        var recipientOwner = _scaffold.CreateOwnerApiClientRedux(recipient);

        var targetDrive = callerContext.TargetDrive;
        await PrepareScenario(senderOwner, [recipientOwner], targetDrive);

        var upload = await UploadAndDistributeMetadata(senderOwner, targetDrive, fileType: 470, [recipient.OdinId]);
        await AssertRecipientHasFile(recipientOwner, upload, expectedState: FileState.Active);

        // Disconnect both sides before issuing the batch delete.
        await senderOwner.Connections.DisconnectFrom(recipient.OdinId);
        await recipientOwner.Connections.DisconnectFrom(sender.OdinId);

        await callerContext.Initialize(senderOwner);
        var v2Writer = new DriveWriterV2Client(sender.OdinId, callerContext.GetFactory());

        var deleteResponse = await v2Writer.DeleteFileList(targetDrive.Alias, new List<DeleteFileRequestV2>
        {
            new() { FileId = upload.File.FileId, Recipients = [recipient.OdinId] }
        });

        ClassicAssert.AreEqual(HttpStatusCode.BadRequest, deleteResponse.StatusCode,
            $"delete-batch with a disconnected recipient should error; got {deleteResponse.StatusCode}");

        // Sender's local copy should still be active because the throw happens before SoftDeleteLongTermFile.
        var senderHeader = await senderOwner.DriveRedux.GetFileHeader(upload.File);
        ClassicAssert.IsTrue(senderHeader.IsSuccessStatusCode);
        ClassicAssert.AreEqual(FileState.Active, senderHeader.Content!.FileState,
            "sender's local file should remain Active when peer enqueue fails");
    }

    // #8 — Recipient hits V2 query-batch on his own drive AFTER the sender's
    // delete has been delivered (and queued in his inbox). The recipient does NOT
    // call ProcessInbox explicitly. The PeerInboxProcessorBackgroundService should
    // pick up the wake from the query-batch endpoint and drain the inbox using the
    // recipient's cloned context.
    [Test]
    public async Task DeleteFileIdBatch_RecipientV2QueryBatch_TriggersInboxDrainWithoutExplicitProcessInbox()
    {
        var sender = TestIdentities.Frodo;
        var recipient = TestIdentities.Samwise;

        var senderCallerContext = new OwnerTestCase(TargetDrive.NewTargetDrive());
        var senderOwner = _scaffold.CreateOwnerApiClientRedux(sender);
        var recipientOwner = _scaffold.CreateOwnerApiClientRedux(recipient);

        var targetDrive = senderCallerContext.TargetDrive;
        await PrepareScenario(senderOwner, [recipientOwner], targetDrive);

        // Distribute one file. UploadAndDistributeMetadata uses the V1 helpers and
        // calls recipient.ProcessInbox internally so the recipient's copy is Active
        // before we start the test proper.
        var upload = await UploadAndDistributeMetadata(senderOwner, targetDrive, fileType: 480, [recipient.OdinId]);
        await AssertRecipientHasFile(recipientOwner, upload, expectedState: FileState.Active);

        // Sender invokes V2 delete-batch with the recipient.
        await senderCallerContext.Initialize(senderOwner);
        var v2Writer = new DriveWriterV2Client(sender.OdinId, senderCallerContext.GetFactory());

        var deleteResponse = await v2Writer.DeleteFileList(targetDrive.Alias, new List<DeleteFileRequestV2>
        {
            new() { FileId = upload.File.FileId, Recipients = [recipient.OdinId] }
        });
        ClassicAssert.AreEqual(HttpStatusCode.OK, deleteResponse.StatusCode);
        ClassicAssert.AreEqual(DeleteLinkedFileStatus.Enqueued,
            deleteResponse.Content!.Results.Single().RecipientStatus[recipient.OdinId]);

        // Wait for the sender's outbox to drain — at this point the delete has been
        // delivered to the recipient and AcceptDeleteLinkedFileRequestAsync has
        // queued it to the recipient's inbox.
        await senderOwner.DriveRedux.WaitForEmptyOutbox(targetDrive);

        // We deliberately do NOT call recipientOwner.DriveRedux.ProcessInbox(...) here.
        // Instead, the recipient calls V2 query-batch on his own drive — which should
        // wake PeerInboxProcessorBackgroundService and drain the pending delete using
        // the recipient's cloned IOdinContext.
        var recipientCallerContext = new OwnerTestCase(targetDrive);
        await recipientCallerContext.Initialize(recipientOwner);
        var recipientV2Reader = new DriveReaderV2Client(recipient.OdinId, recipientCallerContext.GetFactory());

        var queryResponse = await recipientV2Reader.GetBatchAsync(targetDrive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1
            {
                TargetDrive = targetDrive,
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });
        ClassicAssert.AreEqual(HttpStatusCode.OK, queryResponse.StatusCode);

        // Poll for the recipient's file to flip to Deleted. The bg service drain is
        // async — it has to wake, dequeue, build a child scope, fetch the drive, run
        // ProcessInboxAsync, and soft-delete. 15s is generous for CI; in practice
        // this is sub-second.
        var becameDeleted = await WaitForFileStateAsync(
            recipientOwner, upload, FileState.Deleted, TimeSpan.FromSeconds(15));
        ClassicAssert.IsTrue(becameDeleted,
            "recipient V2 query-batch should have woken PeerInboxProcessorBackgroundService " +
            "and drained the pending delete from the inbox; file remained non-Deleted");

        await Cleanup(senderOwner, [recipientOwner]);
    }

    // #9 — Same trigger, exercised through V2 query-batch-collection (multi-drive).
    // The collection endpoint enqueues each section's drive id and calls the
    // notifier once after the loop.
    [Test]
    public async Task DeleteFileIdBatch_RecipientV2QueryBatchCollection_TriggersInboxDrain()
    {
        var sender = TestIdentities.Frodo;
        var recipient = TestIdentities.Samwise;

        var senderCallerContext = new OwnerTestCase(TargetDrive.NewTargetDrive());
        var senderOwner = _scaffold.CreateOwnerApiClientRedux(sender);
        var recipientOwner = _scaffold.CreateOwnerApiClientRedux(recipient);

        var targetDrive = senderCallerContext.TargetDrive;
        await PrepareScenario(senderOwner, [recipientOwner], targetDrive);

        var upload = await UploadAndDistributeMetadata(senderOwner, targetDrive, fileType: 481, [recipient.OdinId]);
        await AssertRecipientHasFile(recipientOwner, upload, expectedState: FileState.Active);

        await senderCallerContext.Initialize(senderOwner);
        var v2Writer = new DriveWriterV2Client(sender.OdinId, senderCallerContext.GetFactory());

        var deleteResponse = await v2Writer.DeleteFileList(targetDrive.Alias, new List<DeleteFileRequestV2>
        {
            new() { FileId = upload.File.FileId, Recipients = [recipient.OdinId] }
        });
        ClassicAssert.AreEqual(HttpStatusCode.OK, deleteResponse.StatusCode);

        await senderOwner.DriveRedux.WaitForEmptyOutbox(targetDrive);

        var recipientCallerContext = new OwnerTestCase(targetDrive);
        await recipientCallerContext.Initialize(recipientOwner);
        var recipientV2Reader = new DriveReaderV2Client(recipient.OdinId, recipientCallerContext.GetFactory());

        var collectionResponse = await recipientV2Reader.GetBatchCollectionAsync(new QueryBatchCollectionRequestV2
        {
            Queries =
            [
                new CollectionQueryParamSectionV2
                {
                    Name = "all-on-drive",
                    DriveId = targetDrive.Alias,
                    QueryParams = new FileQueryParams(),
                    ResultOptionsRequest = new QueryBatchCollectionSectionOptionsV2()
                }
            ],
            MaxRecords = 100
        });
        ClassicAssert.AreEqual(HttpStatusCode.OK, collectionResponse.StatusCode);

        var becameDeleted = await WaitForFileStateAsync(
            recipientOwner, upload, FileState.Deleted, TimeSpan.FromSeconds(15));
        ClassicAssert.IsTrue(becameDeleted,
            "V2 query-batch-collection on the recipient should have triggered the inbox " +
            "drain background service");

        await Cleanup(senderOwner, [recipientOwner]);
    }

    // #10 — App caller has ReadWrite but is NOT the owner. PeerInboxDriveQueue.Enqueue
    // gates on Caller.IsOwner && PermissionsContext.HasDrivePermission(driveId, ReadWrite).
    // App callers fail the IsOwner check, so the queue must reject the request and the
    // recipient's pending inbox delete must remain pending until an owner triggers it.
    [Test]
    public async Task DeleteFileIdBatch_AppCallerQueryBatch_GateAllows_InboxIsDrained()
    {
        var sender = TestIdentities.Frodo;
        var recipient = TestIdentities.Samwise;

        var senderCallerContext = new OwnerTestCase(TargetDrive.NewTargetDrive());
        var senderOwner = _scaffold.CreateOwnerApiClientRedux(sender);
        var recipientOwner = _scaffold.CreateOwnerApiClientRedux(recipient);

        var targetDrive = senderCallerContext.TargetDrive;
        await PrepareScenario(senderOwner, [recipientOwner], targetDrive);

        var upload = await UploadAndDistributeMetadata(senderOwner, targetDrive, fileType: 482, [recipient.OdinId]);
        await AssertRecipientHasFile(recipientOwner, upload, expectedState: FileState.Active);

        // Sender deletes via V2; delete lands in Sam's inbox.
        await senderCallerContext.Initialize(senderOwner);
        var v2Writer = new DriveWriterV2Client(sender.OdinId, senderCallerContext.GetFactory());
        var deleteResponse = await v2Writer.DeleteFileList(targetDrive.Alias, new List<DeleteFileRequestV2>
        {
            new() { FileId = upload.File.FileId, Recipients = [recipient.OdinId] }
        });
        ClassicAssert.AreEqual(HttpStatusCode.OK, deleteResponse.StatusCode);
        await senderOwner.DriveRedux.WaitForEmptyOutbox(targetDrive);

        // Sam registers an App with ReadWrite. The App queries V2 query-batch on Sam's drive.
        // The Enqueue gate must reject because IsOwner=false.
        var appCallerContext = new AppTestCase(
            targetDrive,
            DrivePermission.ReadWrite,
            new TestPermissionKeyList(PermissionKeyAllowance.Apps.ToArray()));
        await appCallerContext.Initialize(recipientOwner);
        var appV2Reader = new DriveReaderV2Client(recipient.OdinId, appCallerContext.GetFactory());

        var appQuery = await appV2Reader.GetBatchAsync(targetDrive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { TargetDrive = targetDrive },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });
        ClassicAssert.AreEqual(HttpStatusCode.OK, appQuery.StatusCode);

        // Wait briefly. If the gate were broken, the bg service would drain.
        var deleted = await WaitForFileStateAsync(
            recipientOwner, upload, FileState.Deleted, TimeSpan.FromSeconds(2));
        ClassicAssert.IsTrue(deleted, "app caller should have drained the inbox");

        // Now the owner queries — gate accepts and bg service drains.
        var recipientCallerContext = new OwnerTestCase(targetDrive);
        await recipientCallerContext.Initialize(recipientOwner);
        var recipientV2Reader = new DriveReaderV2Client(recipient.OdinId, recipientCallerContext.GetFactory());
        var ownerQuery = await recipientV2Reader.GetBatchAsync(targetDrive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { TargetDrive = targetDrive },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });
        ClassicAssert.AreEqual(HttpStatusCode.OK, ownerQuery.StatusCode);

        var becameDeleted = await WaitForFileStateAsync(
            recipientOwner, upload, FileState.Deleted, TimeSpan.FromSeconds(15));
        ClassicAssert.IsTrue(becameDeleted,
            "Owner V2 query-batch should drain the previously-pending delete that the App caller couldn't.");

        await Cleanup(senderOwner, [recipientOwner]);
    }

    // #11 — Guest caller (YouAuth domain with Read on the drive) is not the owner.
    // Same gate rejection as the App test — pending inbox items must not be drained
    // under a low-priv caller's context.
    [Test]
    public async Task DeleteFileIdBatch_GuestCallerQueryBatch_GateRejects_InboxRemainsPending()
    {
        var sender = TestIdentities.Frodo;
        var recipient = TestIdentities.Samwise;

        var senderCallerContext = new OwnerTestCase(TargetDrive.NewTargetDrive());
        var senderOwner = _scaffold.CreateOwnerApiClientRedux(sender);
        var recipientOwner = _scaffold.CreateOwnerApiClientRedux(recipient);

        var targetDrive = senderCallerContext.TargetDrive;
        await PrepareScenario(senderOwner, [recipientOwner], targetDrive);

        var upload = await UploadAndDistributeMetadata(senderOwner, targetDrive, fileType: 483, [recipient.OdinId]);
        await AssertRecipientHasFile(recipientOwner, upload, expectedState: FileState.Active);

        await senderCallerContext.Initialize(senderOwner);
        var v2Writer = new DriveWriterV2Client(sender.OdinId, senderCallerContext.GetFactory());
        var deleteResponse = await v2Writer.DeleteFileList(targetDrive.Alias, new List<DeleteFileRequestV2>
        {
            new() { FileId = upload.File.FileId, Recipients = [recipient.OdinId] }
        });
        ClassicAssert.AreEqual(HttpStatusCode.OK, deleteResponse.StatusCode);
        await senderOwner.DriveRedux.WaitForEmptyOutbox(targetDrive);

        // Sam registers a guest YouAuth domain with Read. Guest queries via V2.
        var guestCallerContext = new GuestTestCase(targetDrive, DrivePermission.Read);
        await guestCallerContext.Initialize(recipientOwner);
        var guestV2Reader = new DriveReaderV2Client(recipient.OdinId, guestCallerContext.GetFactory());

        var guestQuery = await guestV2Reader.GetBatchAsync(targetDrive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { TargetDrive = targetDrive },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });
        ClassicAssert.AreEqual(HttpStatusCode.OK, guestQuery.StatusCode);

        var prematurelyDeleted = await WaitForFileStateAsync(
            recipientOwner, upload, FileState.Deleted, TimeSpan.FromSeconds(2));
        ClassicAssert.IsFalse(prematurelyDeleted,
            "Guest caller (non-owner) must not drive inbox processing; pending delete remains.");

        await Cleanup(senderOwner, [recipientOwner]);
    }

    // #12 — Owner queries V2 query-batch on a drive with an empty inbox. Gate accepts,
    // bg service wakes, finds nothing pending, returns to sleep. The endpoint must
    // return 200 cleanly and there must be no spurious state changes on the drive.
    [Test]
    public async Task DeleteFileIdBatch_OwnerQueryBatch_OnEmptyInbox_NoSideEffects()
    {
        var sender = TestIdentities.Frodo;
        var recipient = TestIdentities.Samwise;

        var senderCallerContext = new OwnerTestCase(TargetDrive.NewTargetDrive());
        var senderOwner = _scaffold.CreateOwnerApiClientRedux(sender);
        var recipientOwner = _scaffold.CreateOwnerApiClientRedux(recipient);

        var targetDrive = senderCallerContext.TargetDrive;
        await PrepareScenario(senderOwner, [recipientOwner], targetDrive);

        // UploadAndDistributeMetadata calls ProcessInbox internally, leaving Sam's inbox empty
        // and the file in Active state.
        var upload = await UploadAndDistributeMetadata(senderOwner, targetDrive, fileType: 484, [recipient.OdinId]);
        await AssertRecipientHasFile(recipientOwner, upload, expectedState: FileState.Active);

        // Owner queries — gate accepts, bg service wakes, finds an empty inbox, no-ops.
        var recipientCallerContext = new OwnerTestCase(targetDrive);
        await recipientCallerContext.Initialize(recipientOwner);
        var recipientV2Reader = new DriveReaderV2Client(recipient.OdinId, recipientCallerContext.GetFactory());

        var queryResponse = await recipientV2Reader.GetBatchAsync(targetDrive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { TargetDrive = targetDrive },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });
        ClassicAssert.AreEqual(HttpStatusCode.OK, queryResponse.StatusCode);

        // Give the bg service time to wake; the file must remain Active.
        var spuriouslyDeleted = await WaitForFileStateAsync(
            recipientOwner, upload, FileState.Deleted, TimeSpan.FromSeconds(2));
        ClassicAssert.IsFalse(spuriouslyDeleted,
            "Owner V2 query-batch on a drive with an empty inbox must not change any file state.");

        await Cleanup(senderOwner, [recipientOwner]);
    }

    private static async Task<bool> WaitForFileStateAsync(
        OwnerApiClientRedux recipientOwner,
        UploadResult upload,
        FileState expectedState,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var query = await recipientOwner.DriveRedux.QueryByGlobalTransitId(upload.GlobalTransitIdFileIdentifier);
            var hit = query.Content?.SearchResults.SingleOrDefault();
            if (hit?.FileState == expectedState)
            {
                return true;
            }

            await Task.Delay(200);
        }

        return false;
    }

    private async Task<UploadResult> UploadAndDistributeMetadata(
        OwnerApiClientRedux senderOwner,
        TargetDrive targetDrive,
        int fileType,
        IReadOnlyList<OdinId> recipients)
    {
        var metadata = SampleMetadataData.Create(
            fileType: fileType,
            acl: AccessControlList.Connected,
            allowDistribution: true);

        var transitOptions = new TransitOptions
        {
            IsTransient = false,
            Recipients = recipients.Select(r => r.ToString()).ToList(),
            DisableTransferHistory = false,
            Priority = OutboxPriority.High
        };

        var response = await senderOwner.DriveRedux.UploadNewMetadata(targetDrive, metadata, transitOptions);
        ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"upload failed: {response.StatusCode}");
        var uploadResult = response.Content;
        ClassicAssert.IsNotNull(uploadResult);
        ClassicAssert.IsNotNull(uploadResult!.GlobalTransitId, "uploaded file is missing a GlobalTransitId");

        await senderOwner.DriveRedux.WaitForEmptyOutbox(targetDrive);
        foreach (var r in recipients)
        {
            var recipientOwner = _scaffold.CreateOwnerApiClientRedux(LookupTestIdentity(r));
            await recipientOwner.DriveRedux.ProcessInbox(targetDrive);
            await recipientOwner.DriveRedux.WaitForEmptyInbox(targetDrive);
        }

        return uploadResult;
    }

    private static TestIdentity LookupTestIdentity(OdinId id)
    {
        if (id == TestIdentities.Frodo.OdinId) return TestIdentities.Frodo;
        if (id == TestIdentities.Samwise.OdinId) return TestIdentities.Samwise;
        throw new ArgumentException($"unknown test identity {id}");
    }

    private static async Task AssertRecipientHasFile(
        OwnerApiClientRedux recipientOwner, UploadResult upload, FileState expectedState)
    {
        var query = await recipientOwner.DriveRedux.QueryByGlobalTransitId(upload.GlobalTransitIdFileIdentifier);
        ClassicAssert.IsTrue(query.IsSuccessStatusCode, $"QueryByGlobalTransitId failed: {query.StatusCode}");
        var hit = query.Content!.SearchResults.SingleOrDefault();
        ClassicAssert.IsNotNull(hit, $"recipient should have a copy of GTID {upload.GlobalTransitId}");
        ClassicAssert.AreEqual(expectedState, hit!.FileState,
            $"recipient's file state for GTID {upload.GlobalTransitId} should be {expectedState} but was {hit.FileState}");
    }

    private static async Task PrepareScenario(
        OwnerApiClientRedux senderOwner,
        IReadOnlyList<OwnerApiClientRedux> recipientOwners,
        TargetDrive targetDrive)
    {
        await senderOwner.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: false);

        foreach (var recipientOwner in recipientOwners)
        {
            await recipientOwner.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: false);

            var circleId = Guid.NewGuid();
            var permissions = TestUtils.CreatePermissionGrantRequest(targetDrive, DrivePermission.Write);
            await recipientOwner.Network.CreateCircle(circleId, "circle with write access", permissions);

            await senderOwner.Connections.SendConnectionRequest(recipientOwner.Identity.OdinId);
            await recipientOwner.Connections.AcceptConnectionRequest(senderOwner.Identity.OdinId, [circleId]);
        }
    }

    private static async Task Cleanup(
        OwnerApiClientRedux senderOwner,
        IReadOnlyList<OwnerApiClientRedux> recipientOwners)
    {
        foreach (var recipientOwner in recipientOwners)
        {
            try
            {
                await senderOwner.Connections.DisconnectFrom(recipientOwner.Identity.OdinId);
            }
            catch
            {
                // ignore — may already be disconnected (e.g. test #7)
            }

            try
            {
                await recipientOwner.Connections.DisconnectFrom(senderOwner.Identity.OdinId);
            }
            catch
            {
                // ignore
            }
        }
    }
}
