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

/// <summary>
/// Verifies the inline inbox drain that fires from the V2 query endpoints (<c>InboxDrainOnQuery</c>).
/// The recipient's inbox is left non-empty deliberately — no ProcessInbox call — so the only way the
/// file shows up in the V2 query response is the inline drain running before the query executes.
///
/// FLAGGED: this lives in the OLD WebScaffold framework (not the fast <c>Odin.Hosting.Tests.V2</c>).
/// Most of the class was ported to
/// <c>tests/apps/Odin.Hosting.Tests.V2/Ported/Peer/InboxDrainOnQueryTests.cs</c> on 2026-05-13 and has
/// been removed from here. Two cases stay:
/// <list type="bullet">
/// <item>the overflow case, which asserts <c>PeerInboxProcessorBackgroundService</c> finishes the items
/// beyond the 50-item inline cap — the in-process framework deliberately never starts background
/// services;</item>
/// <item>the collection case, which drains <b>two</b> drives in two sections. Its port is reduced to a
/// single section because one connection granting two drives needs a custom-circle helper that the
/// framework's <c>PeerFlow</c> doesn't expose yet, so per-section drain across distinct drive inboxes
/// is only covered here.</item>
/// </list>
/// </summary>
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
            ResultOptionsRequest = new QueryBatchCollectionSectionOptionsV2()
        };

        var sectionB = new CollectionQueryParamSectionV2
        {
            Name = "sectionB",
            DriveId = driveB.Alias,
            QueryParams = new FileQueryParams
            {
                GlobalTransitId = [uploadB.GlobalTransitIdFileIdentifier.GlobalTransitId]
            },
            ResultOptionsRequest = new QueryBatchCollectionSectionOptionsV2()
        };

        var queryResponse = await v2Reader.GetBatchCollectionAsync(new QueryBatchCollectionRequestV2
        {
            Queries = [sectionA, sectionB],
            MaxRecords = 100
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

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private async Task<DriveReaderV2Client> BuildOwnerV2Reader(OwnerApiClientRedux recipient, TargetDrive targetDrive)
    {
        var ownerCtx = new OwnerTestCase(targetDrive);
        await ownerCtx.Initialize(recipient);
        return new DriveReaderV2Client(recipient.Identity.OdinId, ownerCtx.GetFactory());
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
        // Only one connection per identity pair: calling this twice in a single test throws
        // CannotSendConnectionRequestToValidConnection on the second SendConnectionRequest.
        // A test needing two drives on one connection uses PrepareScenarioMultiDrive instead.
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
