using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Hosting.UnifiedV2.Drive.Read;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// Port of <c>_V2/Tests/Drive/DriveReaderTests/InboxDrainOnQueryTests</c>. Verifies the inline
/// inbox drain that fires from the V2 query endpoints (<c>InboxDrainOnQuery</c> in production).
/// The recipient's inbox is left non-empty deliberately — no explicit <c>ProcessInbox</c> call —
/// so the only way the file shows up in the V2 query response is the inline drain running before
/// the query executes.
/// </summary>
/// <remarks>
/// The overflow / background-completion case from the original test is omitted: it asserts that
/// <c>PeerInboxProcessorBackgroundService</c> drains items beyond the 50-item inline cap, but the
/// in-process framework deliberately doesn't start background services. The other five inline-drain
/// scenarios cover the contract.
/// </remarks>
[TestFixture]
public class InboxDrainOnQueryTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task QueryBatch_DrainsInbox_OnRecipient()
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(sender, recipient, DrivePermission.Write, "inbox-drain qb");

        var gtid = await SendFileAsync(sender, recipient, drive, fileType: 7771);

        // Sender's outbox drains (peer delivery). We deliberately do NOT call
        // recipient.Sync.ProcessInboxAsync — the V2 query path is responsible for draining inline.
        await sender.Sync.DrainOutboxAsync();

        var queryResponse = await recipient.Drives.Reader.GetBatchAsync(drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1
            {
                TargetDrive = drive,
                GlobalTransitId = [gtid]
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });

        Assert.That(queryResponse.IsSuccessStatusCode, Is.True, $"got {queryResponse.StatusCode}");
        var hit = queryResponse.Content!.SearchResults.SingleOrDefault();
        Assert.That(hit, Is.Not.Null,
            "Recipient's V2 GetBatch should see the file because InboxDrainOnQuery drained the inbox inline.");
    }

    [Test]
    public async Task QuerySmartBatch_DrainsInbox_OnRecipient()
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(sender, recipient, DrivePermission.Write, "inbox-drain qsb");

        var gtid = await SendFileAsync(sender, recipient, drive, fileType: 7772);
        await sender.Sync.DrainOutboxAsync();

        var queryResponse = await recipient.Drives.Reader.GetSmartBatchAsync(drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1
            {
                TargetDrive = drive,
                GlobalTransitId = [gtid]
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });

        Assert.That(queryResponse.IsSuccessStatusCode, Is.True, $"got {queryResponse.StatusCode}");
        var hit = queryResponse.Content!.SearchResults.SingleOrDefault();
        Assert.That(hit, Is.Not.Null,
            "Recipient's V2 GetSmartBatch should see the file because InboxDrainOnQuery drained the inbox inline.");
    }

    [Test]
    public async Task QueryBatchCollection_DrainsInbox_PerSection_OnRecipient()
    {
        // Simplified vs the original two-drive collection: a single section still exercises the
        // per-section drain path in V2DriveBatchQueryController (it calls DrainIfReadyAsync inside
        // its foreach over sections). Building a single connection that grants two drives needs a
        // custom circle helper which the in-process framework's PeerFlow doesn't currently expose;
        // skipping the second-drive section keeps the value of the test (drain runs per section
        // before that section's query) without that helper.
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(sender, recipient, DrivePermission.Write, "inbox-drain coll");

        var gtid = await SendFileAsync(sender, recipient, drive, fileType: 7773);
        await sender.Sync.DrainOutboxAsync();

        var queryResponse = await recipient.Drives.Reader.GetBatchCollectionAsync(new QueryBatchCollectionRequestV2
        {
            Queries =
            [
                new CollectionQueryParamSectionV2
                {
                    Name = "sectionA",
                    DriveId = drive.Alias,
                    QueryParams = new FileQueryParams { GlobalTransitId = [gtid] },
                    ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
                }
            ]
        });

        Assert.That(queryResponse.IsSuccessStatusCode, Is.True, $"got {queryResponse.StatusCode}");
        var section = queryResponse.Content!.Results.SingleOrDefault(r => r.Name == "sectionA");
        Assert.That(section, Is.Not.Null);
        Assert.That(section!.SearchResults.SingleOrDefault(), Is.Not.Null,
            "Section should drain the drive's inbox inline before its query runs.");
    }

    [Test]
    public async Task QueryBatch_EmptyInbox_DoesNotError()
    {
        // Hot path: drive exists, inbox is empty. InboxDrainOnQuery should bail out at the
        // GetReadyCountAsync<=0 gate without touching ProcessInboxAsync. We can't observe
        // "didn't call processor" directly, but we can assert the query returns successfully
        // and with zero results on a drive that's never received a peer transfer.
        var owner = await LoginAsOwner(Identities.Sam);
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "empty inbox drive");

        var queryResponse = await owner.Drives.Reader.GetBatchAsync(drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1
            {
                TargetDrive = drive,
                FileType = [9999]
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });

        Assert.That(queryResponse.IsSuccessStatusCode, Is.True, $"got {queryResponse.StatusCode}");
        Assert.That(queryResponse.Content!.SearchResults.Count(), Is.EqualTo(0));
    }

    [Test]
    public async Task GuestCaller_DoesNotTriggerInlineDrain_AndDoesNotDropInboxItems()
    {
        // Regression guard: a non-owner caller must not drive inbox processing on the V2 query
        // path. Without InboxDrainOnQuery's auth gate, the helper would call ProcessInboxAsync
        // under the guest's context — which throws inside the storage layer and causes
        // PeerInboxProcessor to silently DeleteFromInbox the item. The test proves the inbox
        // item survives a guest query and is still processable by the owner afterwards.
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(sender, recipient, DrivePermission.Write, "guest-no-drain");

        var gtid = await SendFileAsync(sender, recipient, drive, fileType: 7790);
        await sender.Sync.DrainOutboxAsync();

        // Build a guest session attached to the recipient, granted Read on the existing peer drive.
        var guest = await Api.GuestSession.SetupAsync(recipient, drive, DrivePermission.Read);

        var guestQuery = await guest.Drives.Reader.GetBatchAsync(drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1
            {
                TargetDrive = drive,
                GlobalTransitId = [gtid]
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });
        Assert.That(guestQuery.IsSuccessStatusCode, Is.True, $"got {guestQuery.StatusCode}");
        Assert.That(guestQuery.Content!.SearchResults.Count(), Is.EqualTo(0),
            "Guest query should see no file yet — drain was skipped, so the inbox item is still pending.");

        // Owner now queries via V2. This time InboxDrainOnQuery runs (caller is owner) and the
        // file becomes visible. If the guest call had silently dropped the item, this would fail.
        var ownerQuery = await recipient.Drives.Reader.GetBatchAsync(drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1
            {
                TargetDrive = drive,
                GlobalTransitId = [gtid]
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });
        Assert.That(ownerQuery.IsSuccessStatusCode, Is.True);
        var hit = ownerQuery.Content!.SearchResults.SingleOrDefault();
        Assert.That(hit, Is.Not.Null,
            "Owner's V2 query should drain inline and surface the file. If null, the earlier guest " +
            "query silently dropped the item from the inbox.");
    }

    // -----------------------------------------------------------------------------------------
    // helpers
    // -----------------------------------------------------------------------------------------

    private static async Task<Guid> SendFileAsync(OwnerSession sender, OwnerSession recipient, TargetDrive drive, int fileType)
    {
        var metadata = SampleMetadataData.Create(fileType: fileType, acl: AccessControlList.Connected, allowDistribution: true);
        var transit = new TransitOptions
        {
            Recipients = new List<string> { recipient.Identity }
        };

        var response = await sender.Drives.Writer.UploadNewMetadata(drive.Alias, metadata, transit);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"upload failed: {response.StatusCode}");
        return response.Content!.GlobalTransitId
            ?? throw new InvalidOperationException("distribution upload must yield a GlobalTransitId");
    }
}
