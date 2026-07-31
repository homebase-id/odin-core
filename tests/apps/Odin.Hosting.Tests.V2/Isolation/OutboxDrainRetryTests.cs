#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Isolation;

/// <summary>
/// <c>DrainOutboxAsync</c> must not report "done" while delivery is still owed.
///
/// A drain pass fans every eligible item out concurrently, so transient failures happen — two peer
/// writes racing for the recipient's SQLite write lock is the common one, which surfaces as a 500 and
/// <c>RecipientIdentityReturnedServerError</c> on the sender. The worker then reschedules the item
/// behind a retry backoff (10s+), which made the queue look idle to
/// <c>TableOutbox.CheckOutItemAsync</c> — it only checks out rows with <c>nextRunTime &lt;= now</c>.
/// The drain returned, the test processed an empty inbox, and the assertion saw stale state.
///
/// This pins the fix: the drain brings backed-off items forward and retries them.
/// </summary>
[TestFixture]
public class OutboxDrainRetryTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task DrainOutbox_DeliversItemsParkedBehindARetryBackoff()
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(sender, recipient, DrivePermission.Write, "drain-retry");

        // Clear the outbox items left by the connection handshake so the only thing queued below is
        // the upload's own transfer item — otherwise we'd park an unrelated row and prove nothing.
        await sender.Sync.DrainOutboxAsync();

        var metadata = SampleMetadataData.Create(fileType: 8801, acl: AccessControlList.Connected);
        metadata.AllowDistribution = true;
        var upload = await sender.Drives.Writer.UploadNewMetadata(drive.Alias, metadata,
            transitOptions: new TransitOptions { Recipients = [recipient.Identity] });
        Assert.That(upload.IsSuccessStatusCode, Is.True, $"upload failed: {upload.StatusCode}");
        var gtid = upload.Content!.GlobalTransitId!.Value;

        // Simulate what a transient send failure leaves behind: the item checked back in with its
        // next attempt parked well past any drain the test could reasonably wait out. Done through
        // the same check-out/check-in-as-cancelled pair the outbox worker uses on failure.
        var tblOutbox = Host.GetTenantScope(sender.Identity.DomainName).Resolve<TableOutbox>();
        var checkedOut = await tblOutbox.CheckOutItemAsync();
        Assert.That(checkedOut, Is.Not.Null, "precondition: the upload should have queued an outbox item");
        Assert.That(checkedOut!.driveId, Is.EqualTo(drive.Alias.Value),
            "precondition: the checked-out item must be this drive's transfer item");
        Assert.That(checkedOut.fileId, Is.EqualTo(upload.Content.FileId),
            "precondition: the checked-out item must be the file we just uploaded");

        var parkedUntil = UnixTimeUtc.Now().AddSeconds(600);
        await tblOutbox.CheckInAsCancelledAsync(new System.Guid(checkedOut.checkOutStamp!.Value.ToByteArray()), parkedUntil);

        Assert.That(await sender.Sync.IsOutboxEmptyAsync(drive), Is.False,
            "precondition: the parked item must still be queued on this drive");
        var parked = await tblOutbox.NextScheduledItemAsync(drive.Alias);
        Assert.That(parked, Is.Not.Null);
        Assert.That(parked!.Value.milliseconds, Is.GreaterThan(UnixTimeUtc.Now().milliseconds),
            "precondition: the item must be scheduled in the future for this to test anything");

        await sender.Sync.DrainOutboxAsync();
        await recipient.Sync.ProcessInboxAsync(drive);

        Assert.That(await sender.Sync.IsOutboxEmptyAsync(drive), Is.True,
            "drain must not leave a backed-off item behind");

        var q = await recipient.Drives.Reader.GetBatchAsync(drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { GlobalTransitId = [gtid] },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10, IncludeMetadataHeader = true }
        });
        Assert.That(q.IsSuccessStatusCode, Is.True, $"query failed: {q.StatusCode}");
        Assert.That(q.Content!.SearchResults.SingleOrDefault(), Is.Not.Null,
            "the parked item should have been brought forward, retried, and delivered");
    }

    [Test]
    public async Task DrainOutbox_ReturnsWhenNothingIsOwed()
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(sender, recipient, DrivePermission.Write, "drain-empty");

        // Sanity check on the loop's exit condition: an ordinary drain of an ordinary transfer still
        // terminates, and a drain over an already-empty outbox is a no-op rather than a spin.
        var metadata = SampleMetadataData.Create(fileType: 8802, acl: AccessControlList.Connected);
        metadata.AllowDistribution = true;
        var upload = await sender.Drives.Writer.UploadNewMetadata(drive.Alias, metadata,
            transitOptions: new TransitOptions { Recipients = new List<string> { recipient.Identity } });
        Assert.That(upload.IsSuccessStatusCode, Is.True);

        await sender.Sync.DrainOutboxAsync();
        Assert.That(await sender.Sync.IsOutboxEmptyAsync(drive), Is.True);

        await sender.Sync.DrainOutboxAsync();
        Assert.That(await sender.Sync.IsOutboxEmptyAsync(drive), Is.True);
    }
}
