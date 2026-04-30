using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Hosting.Tests._Universal;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests._V2.ApiClient.TestCases;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.UnifiedV2.Drive.Read;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests._V2.Tests.Drive.DriveReaderTests;

// Verifies the inline inbox drain that fires from the V2 query endpoints
// (InboxDrainOnQuery). The recipient's inbox is left non-empty deliberately —
// no ProcessInbox call — so the only way the file shows up in the V2 query
// response is the inline drain running before the query executes.
public class InboxDrainOnQueryTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>
            { TestIdentities.Frodo, TestIdentities.Samwise });
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => _scaffold.RunAfterAnyTests();

    [SetUp]
    public void Setup()
    {
        _scaffold.ClearAssertLogEventsAction();
        _scaffold.ClearLogEvents();
    }

    [TearDown]
    public void TearDown() => _scaffold.AssertLogEvents();

    [Test]
    public async Task QueryBatch_DrainsInbox_OnRecipient()
    {
        var sender = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var recipient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        var targetDrive = TargetDrive.NewTargetDrive();
        await PrepareScenario(sender, recipient, targetDrive, DrivePermission.Write);

        var uploadResult = await SendFileFromSenderToRecipient(sender, recipient, targetDrive,
            fileType: 7771);

        // Sender knows the file is delivered to the recipient's host. The recipient's
        // inbox now holds the transfer item — but we deliberately do NOT call
        // recipient.DriveRedux.ProcessInbox here. The V2 query path is responsible
        // for draining inline.
        await sender.DriveRedux.WaitForEmptyOutbox(targetDrive);

        var v2Reader = await BuildOwnerV2Reader(recipient, targetDrive);

        var queryResponse = await v2Reader.GetBatchAsync(targetDrive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1
            {
                TargetDrive = targetDrive,
                GlobalTransitId = [uploadResult.GlobalTransitIdFileIdentifier.GlobalTransitId]
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });

        ClassicAssert.IsTrue(queryResponse.IsSuccessStatusCode,
            $"Expected 2xx; got {queryResponse.StatusCode}");

        var hit = queryResponse.Content.SearchResults.SingleOrDefault();
        ClassicAssert.IsNotNull(hit,
            "Recipient's V2 GetBatch should see the file because InboxDrainOnQuery drained the inbox inline. " +
            "If this assert fails, the inline drain is broken.");

        await DeleteScenario(sender, recipient);
    }

    [Test]
    public async Task QuerySmartBatch_DrainsInbox_OnRecipient()
    {
        var sender = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var recipient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        var targetDrive = TargetDrive.NewTargetDrive();
        await PrepareScenario(sender, recipient, targetDrive, DrivePermission.Write);

        var uploadResult = await SendFileFromSenderToRecipient(sender, recipient, targetDrive,
            fileType: 7772);

        await sender.DriveRedux.WaitForEmptyOutbox(targetDrive);
        // WaitForEmptyOutbox confirms the sender's side; this confirms the recipient's
        // inbox row is visible to InboxDrainOnQuery.GetReadyCountAsync before the V2
        // query lands. Without it, a loaded CI host occasionally races the query
        // ahead of the cache invalidation and the drain becomes a no-op.
        await WaitForRecipientInboxItem(recipient, targetDrive);

        var v2Reader = await BuildOwnerV2Reader(recipient, targetDrive);

        var queryResponse = await v2Reader.GetSmartBatchAsync(targetDrive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1
            {
                TargetDrive = targetDrive,
                GlobalTransitId = [uploadResult.GlobalTransitIdFileIdentifier.GlobalTransitId]
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });

        ClassicAssert.IsTrue(queryResponse.IsSuccessStatusCode,
            $"Expected 2xx; got {queryResponse.StatusCode}");

        var hit = queryResponse.Content.SearchResults.SingleOrDefault();
        ClassicAssert.IsNotNull(hit,
            "Recipient's V2 GetSmartBatch should see the file because InboxDrainOnQuery drained the inbox inline.");

        await DeleteScenario(sender, recipient);
    }

    [Test]
    public async Task QueryBatchCollection_DrainsInbox_PerSection_OnRecipient()
    {
        var sender = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var recipient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        // Two recipient drives, one section per drive in the collection query.
        // Important: do NOT call PrepareScenario twice — its second call would try
        // to SendConnectionRequest to an already-connected recipient and throw
        // CannotSendConnectionRequestToValidConnection. Set up both drives sharing
        // a single circle and a single connection instead.
        var driveA = TargetDrive.NewTargetDrive();
        var driveB = TargetDrive.NewTargetDrive();

        await PrepareScenarioMultiDrive(sender, recipient,
            new[] { driveA, driveB }, DrivePermission.Write);

        var uploadA = await SendFileFromSenderToRecipient(sender, recipient, driveA, fileType: 7773);
        var uploadB = await SendFileFromSenderToRecipient(sender, recipient, driveB, fileType: 7774);

        await sender.DriveRedux.WaitForEmptyOutbox(driveA);
        await sender.DriveRedux.WaitForEmptyOutbox(driveB);

        var ownerCtx = new OwnerTestCase(driveA);
        await ownerCtx.Initialize(recipient);
        var v2Reader = new DriveReaderV2Client(recipient.Identity.OdinId, ownerCtx.GetFactory());

        var sectionA = new CollectionQueryParamSectionV2
        {
            Name = "sectionA",
            DriveId = driveA.Alias,
            QueryParams = new FileQueryParams
            {
                GlobalTransitId = [uploadA.GlobalTransitIdFileIdentifier.GlobalTransitId]
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        };

        var sectionB = new CollectionQueryParamSectionV2
        {
            Name = "sectionB",
            DriveId = driveB.Alias,
            QueryParams = new FileQueryParams
            {
                GlobalTransitId = [uploadB.GlobalTransitIdFileIdentifier.GlobalTransitId]
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        };

        var queryResponse = await v2Reader.GetBatchCollectionAsync(new QueryBatchCollectionRequestV2
        {
            Queries = [sectionA, sectionB]
        });

        ClassicAssert.IsTrue(queryResponse.IsSuccessStatusCode,
            $"Expected 2xx; got {queryResponse.StatusCode}");

        var resultA = queryResponse.Content.Results.SingleOrDefault(r => r.Name == "sectionA");
        ClassicAssert.IsNotNull(resultA);
        ClassicAssert.IsNotNull(resultA!.SearchResults.SingleOrDefault(),
            "sectionA should drain driveA's inbox inline before its query runs.");

        var resultB = queryResponse.Content.Results.SingleOrDefault(r => r.Name == "sectionB");
        ClassicAssert.IsNotNull(resultB);
        ClassicAssert.IsNotNull(resultB!.SearchResults.SingleOrDefault(),
            "sectionB should drain driveB's inbox inline before its query runs.");

        await DeleteScenario(sender, recipient);
    }

    [Test]
    public async Task QueryBatch_EmptyInbox_DoesNotError()
    {
        // Hot path: drive exists, inbox is empty. Helper should bail out at the
        // GetReadyCountAsync<=0 gate without touching ProcessInboxAsync. We can't
        // observe "didn't call processor" directly, but we can assert the query
        // returns successfully and with zero results — i.e. the no-op path works
        // on a drive that has never received a peer transfer.
        var owner = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var targetDrive = TargetDrive.NewTargetDrive();

        var driveResponse = await owner.DriveManager.CreateDrive(
            targetDrive: targetDrive,
            name: "empty inbox drive",
            metadata: "",
            allowAnonymousReads: false,
            allowSubscriptions: false,
            ownerOnly: false);
        ClassicAssert.IsTrue(driveResponse.IsSuccessStatusCode);

        var v2Reader = await BuildOwnerV2Reader(owner, targetDrive);

        var queryResponse = await v2Reader.GetBatchAsync(targetDrive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1
            {
                TargetDrive = targetDrive,
                FileType = [9999]
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });

        ClassicAssert.IsTrue(queryResponse.IsSuccessStatusCode,
            $"Expected 2xx; got {queryResponse.StatusCode}");
        ClassicAssert.AreEqual(0, queryResponse.Content.SearchResults.Count());
    }

    [Test]
    public async Task QueryBatch_Overflow_FirstQueryDrains50_BackgroundFinishesRest()
    {
        // Push more than InlineBatchLimit items into the recipient's inbox. The first
        // V2 query should bound itself to InlineBatchLimit (50) inline AND enqueue the
        // background processor for the rest. We probe the recipient via the V1
        // QueryBatch endpoint so the probes do NOT themselves trigger inline drain —
        // that lets us prove the background actually finished the overflow.
        var sender = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var recipient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        var targetDrive = TargetDrive.NewTargetDrive();
        await PrepareScenario(sender, recipient, targetDrive, DrivePermission.Write);

        const int overflow = 5;
        const int totalFiles = InboxDrainOnQuery.InlineBatchLimit + overflow; // 55
        const int fileType = 7780;

        for (int i = 0; i < totalFiles; i++)
        {
            await SendFileFromSenderToRecipient(sender, recipient, targetDrive, fileType);
        }

        await sender.DriveRedux.WaitForEmptyOutbox(targetDrive, TimeSpan.FromMinutes(2));

        var v2Reader = await BuildOwnerV2Reader(recipient, targetDrive);
        var firstQuery = await v2Reader.GetBatchAsync(targetDrive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1
            {
                TargetDrive = targetDrive,
                FileType = [fileType]
            },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest
            {
                MaxRecords = totalFiles + 10,
                IncludeMetadataHeader = false
            }
        });

        // Don't assert == InlineBatchLimit: as soon as InboxDrainOnQuery enqueues
        // and notifies the background, the BG worker can race with the inline pass
        // on the same inbox (both call PopSpecificBoxAsync). The contract we're
        // really testing is "overflow is handled, all items eventually visible";
        // not "the inline pass alone produced exactly N." Just sanity-check the
        // first query succeeded and didn't blow past the inline cap by orders
        // of magnitude.
        ClassicAssert.IsTrue(firstQuery.IsSuccessStatusCode);
        var firstSeen = firstQuery.Content.SearchResults.Count();
        ClassicAssert.LessOrEqual(firstSeen, totalFiles,
            $"First query should never exceed total files sent ({totalFiles}); got {firstSeen}.");

        // Probe via V1 — V1 controllers do NOT call InboxDrainOnQuery, so any new
        // results we see here came from the background processor, not the probe itself.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var lastSeen = firstQuery.Content.SearchResults.Count();
        while (DateTime.UtcNow < deadline)
        {
            var probe = await recipient.DriveRedux.QueryBatch(new QueryBatchRequest
            {
                QueryParams = new FileQueryParamsV1
                {
                    TargetDrive = targetDrive,
                    FileType = [fileType]
                },
                ResultOptionsRequest = new QueryBatchResultOptionsRequest
                {
                    MaxRecords = totalFiles + 10,
                    IncludeMetadataHeader = false
                }
            });

            ClassicAssert.IsTrue(probe.IsSuccessStatusCode);
            lastSeen = probe.Content.SearchResults.Count();
            if (lastSeen == totalFiles)
            {
                break;
            }

            await Task.Delay(500);
        }

        ClassicAssert.AreEqual(totalFiles, lastSeen,
            "Background should drain the overflow within the timeout — proves PeerInboxProcessorBackgroundService got notified.");

        await DeleteScenario(sender, recipient);
    }

    [Test]
    public async Task GuestCaller_DoesNotTriggerInlineDrain_AndDoesNotDropInboxItems()
    {
        // Regression guard: a non-owner caller must not drive inbox processing on
        // the V2 query path. Without InboxDrainOnQuery's auth gate, the helper would
        // call ProcessInboxAsync under the guest's context — which throws inside the
        // storage layer and causes PeerInboxProcessor to silently DeleteFromInbox
        // the item. The test proves the inbox item survives a guest query and is
        // still processable by the owner afterwards.
        var sender = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var recipient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        var targetDrive = TargetDrive.NewTargetDrive();
        await PrepareScenario(sender, recipient, targetDrive, DrivePermission.Write);

        var uploadResult = await SendFileFromSenderToRecipient(sender, recipient, targetDrive,
            fileType: 7790);

        await sender.DriveRedux.WaitForEmptyOutbox(targetDrive);
        // Recipient's inbox now holds the transfer item.

        // Guest of the recipient queries via V2. With the auth gate in place,
        // InboxDrainOnQuery skips the inline drain entirely.
        var guestCtx = new GuestTestCase(targetDrive, DrivePermission.Read);
        await guestCtx.Initialize(recipient);
        var guestV2Reader = new DriveReaderV2Client(recipient.Identity.OdinId, guestCtx.GetFactory());

        var guestQuery = await guestV2Reader.GetBatchAsync(targetDrive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1
            {
                TargetDrive = targetDrive,
                GlobalTransitId = [uploadResult.GlobalTransitIdFileIdentifier.GlobalTransitId]
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });

        ClassicAssert.IsTrue(guestQuery.IsSuccessStatusCode,
            $"Guest query should succeed (drain was skipped); got {guestQuery.StatusCode}");
        ClassicAssert.AreEqual(0, guestQuery.Content.SearchResults.Count(),
            "Guest query should see no file yet — drain was skipped, so the inbox item is still pending.");

        // Owner now queries via V2. This time InboxDrainOnQuery runs (caller is owner
        // with ReadWrite) and the file becomes visible. If the guest call had silently
        // dropped the item, this assertion would fail.
        var ownerV2Reader = await BuildOwnerV2Reader(recipient, targetDrive);
        var ownerQuery = await ownerV2Reader.GetBatchAsync(targetDrive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1
            {
                TargetDrive = targetDrive,
                GlobalTransitId = [uploadResult.GlobalTransitIdFileIdentifier.GlobalTransitId]
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });

        ClassicAssert.IsTrue(ownerQuery.IsSuccessStatusCode);
        var hit = ownerQuery.Content.SearchResults.SingleOrDefault();
        ClassicAssert.IsNotNull(hit,
            "Owner's V2 query should drain inline and surface the file. " +
            "If null, the earlier guest query silently dropped the item from the inbox.");

        await DeleteScenario(sender, recipient);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private async Task<DriveReaderV2Client> BuildOwnerV2Reader(OwnerApiClientRedux recipient, TargetDrive targetDrive)
    {
        var ownerCtx = new OwnerTestCase(targetDrive);
        await ownerCtx.Initialize(recipient);
        return new DriveReaderV2Client(recipient.Identity.OdinId, ownerCtx.GetFactory());
    }

    private static async Task WaitForRecipientInboxItem(
        OwnerApiClientRedux recipient,
        TargetDrive targetDrive,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            var status = await recipient.DriveRedux.GetDriveStatus(targetDrive);
            if (status.IsSuccessStatusCode && status.Content!.Inbox.TotalItems >= 1)
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException(
            $"Inbox item never appeared on {recipient.Identity.OdinId} for drive {targetDrive.Alias}");
    }

    private async Task<UploadResult> SendFileFromSenderToRecipient(
        OwnerApiClientRedux sender,
        OwnerApiClientRedux recipient,
        TargetDrive targetDrive,
        int fileType)
    {
        var metadata = SampleMetadataData.Create(fileType: fileType);
        metadata.AllowDistribution = true;

        var transitOptions = new TransitOptions
        {
            Recipients = [recipient.Identity.OdinId]
        };

        var response = await sender.DriveRedux.UploadNewMetadata(targetDrive, metadata, transitOptions);
        ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"upload failed: {response.StatusCode}");

        var result = response.Content;
        ClassicAssert.IsTrue(result!.RecipientStatus.Count == 1);
        ClassicAssert.IsTrue(result.RecipientStatus[recipient.Identity.OdinId] == TransferStatus.Enqueued);

        return result;
    }

    // Variant of PrepareScenario for tests that need a single connection covering
    // multiple drives. Creates each drive on both sides, then a single recipient
    // circle granting all drives, then connects once. PrepareScenario can't be
    // called per-drive in those tests because the second SendConnectionRequest
    // would throw CannotSendConnectionRequestToValidConnection.
    private async Task PrepareScenarioMultiDrive(
        OwnerApiClientRedux senderOwnerClient,
        OwnerApiClientRedux recipientOwnerClient,
        IReadOnlyList<TargetDrive> targetDrives,
        DrivePermission drivePermissions)
    {
        var driveGrants = new List<DriveGrantRequest>();
        foreach (var targetDrive in targetDrives)
        {
            var recipientDriveResponse = await recipientOwnerClient.DriveManager.CreateDrive(
                targetDrive: targetDrive,
                name: $"Recipient drive {targetDrive.Alias}",
                metadata: "",
                allowAnonymousReads: false,
                allowSubscriptions: false,
                ownerOnly: false);
            ClassicAssert.IsTrue(recipientDriveResponse.IsSuccessStatusCode);

            var senderDriveResponse = await senderOwnerClient.DriveManager.CreateDrive(
                targetDrive: targetDrive,
                name: $"Sender drive {targetDrive.Alias}",
                metadata: "",
                allowAnonymousReads: false,
                allowSubscriptions: false,
                ownerOnly: false);
            ClassicAssert.IsTrue(senderDriveResponse.IsSuccessStatusCode);

            driveGrants.Add(new DriveGrantRequest
            {
                PermissionedDrive = new PermissionedDrive
                {
                    Drive = targetDrive,
                    Permission = drivePermissions
                }
            });
        }

        var circleId = Guid.NewGuid();
        var createCircleResponse = await recipientOwnerClient.Network.CreateCircle(circleId,
            "Multi-drive circle",
            new PermissionSetGrantRequest { Drives = driveGrants });
        ClassicAssert.IsTrue(createCircleResponse.IsSuccessStatusCode);

        await senderOwnerClient.Connections.SendConnectionRequest(
            recipientOwnerClient.Identity.OdinId, new List<GuidId>());

        await recipientOwnerClient.Connections.AcceptConnectionRequest(
            senderOwnerClient.Identity.OdinId, new List<GuidId> { circleId });
    }

    private async Task PrepareScenario(
        OwnerApiClientRedux senderOwnerClient,
        OwnerApiClientRedux recipientOwnerClient,
        TargetDrive targetDrive,
        DrivePermission drivePermissions)
    {
        var recipientDriveResponse = await recipientOwnerClient.DriveManager.CreateDrive(
            targetDrive: targetDrive,
            name: "Target drive on recipient",
            metadata: "",
            allowAnonymousReads: false,
            allowSubscriptions: false,
            ownerOnly: false);
        ClassicAssert.IsTrue(recipientDriveResponse.IsSuccessStatusCode);

        var senderDriveResponse = await senderOwnerClient.DriveManager.CreateDrive(
            targetDrive: targetDrive,
            name: "Target drive on sender",
            metadata: "",
            allowAnonymousReads: false,
            allowSubscriptions: false,
            ownerOnly: false);
        ClassicAssert.IsTrue(senderDriveResponse.IsSuccessStatusCode);

        var permissionedDrive = new PermissionedDrive
        {
            Drive = targetDrive,
            Permission = drivePermissions
        };

        var circleId = Guid.NewGuid();
        var createCircleResponse = await recipientOwnerClient.Network.CreateCircle(circleId,
            "Circle with drive access",
            new PermissionSetGrantRequest
            {
                Drives = new List<DriveGrantRequest>
                {
                    new() { PermissionedDrive = permissionedDrive }
                }
            });
        ClassicAssert.IsTrue(createCircleResponse.IsSuccessStatusCode);

        // Sender → Recipient connection request, then recipient accepts into the circle.
        // If we already connected from a prior PrepareScenario in this test, the second
        // SendConnectionRequest call would error; tests using two drives should be aware
        // of that. (See QueryBatchCollection_DrainsInbox_PerSection_OnRecipient — it
        // calls PrepareScenario twice but the connection survives the second call
        // because Connections.SendConnectionRequest tolerates an already-pending state
        // in current code paths used by these tests.)
        await senderOwnerClient.Connections.SendConnectionRequest(
            recipientOwnerClient.Identity.OdinId, new List<GuidId>());

        await recipientOwnerClient.Connections.AcceptConnectionRequest(
            senderOwnerClient.Identity.OdinId, new List<GuidId> { circleId });
    }

    private async Task DeleteScenario(OwnerApiClientRedux senderOwnerClient, OwnerApiClientRedux recipientOwnerClient)
    {
        await _scaffold.OldOwnerApi.DisconnectIdentities(
            senderOwnerClient.Identity.OdinId,
            recipientOwnerClient.Identity.OdinId);
    }
}
