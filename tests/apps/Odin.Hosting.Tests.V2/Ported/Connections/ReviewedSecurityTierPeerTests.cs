#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Ported.Connections;

/// <summary>
/// The reviewed security tier (<see cref="TenantConfigFlagNames.UseReviewedSecurityTier"/>) with the flag on.
/// </summary>
/// <remarks>
/// Frodo has the flag on; Sam is connected to Frodo but Frodo has not reviewed him.  The ladder limits what an
/// unreviewed connection can <i>see</i> (content behind a <c>connected</c> ACL), not whether the connection
/// works: Sam is still in the circles that grant him write, so he can still send files, send read receipts,
/// disconnect and verify the connection.
/// <para>
/// Peer plumbing asks <see cref="CallerContext.HasActiveConnection"/>; content ACLs ask the tier
/// (docs/connection-defaults.md, "Connected-but-unreviewed survives as an internal caller classification").
/// The controls (flag off, reviewed connection) and the content test pin down both halves.
/// </para>
/// <para>
/// Not covered here: peer file updates, introductions, the peer app-notification token, and the
/// verification-hash sync push.
/// </para>
/// </remarks>
[TestFixture]
public class ReviewedSecurityTierPeerTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    // -- controls ---------------------------------------------------------------------------------

    [Test]
    public async Task FlagOff_UnreviewedConnection_CanSendAFile()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(sam, frodo, DrivePermission.Write, "flag-off");

        await ClearReviewOnAsync(frodo, sam.Identity);

        var gtid = await SendFileAsync(sam, frodo, drive);

        Assert.That(await CountByGtidAsync(frodo, drive, gtid), Is.EqualTo(1),
            "with the flag off an unreviewed connection sends files exactly as before");
    }

    [Test]
    public async Task FlagOn_ReviewedConnection_CanSendAFile()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(sam, frodo, DrivePermission.Write, "reviewed");

        await EnableReviewedTierAsync(frodo);
        try
        {
            var gtid = await SendFileAsync(sam, frodo, drive);

            Assert.That(await CountByGtidAsync(frodo, drive, gtid), Is.EqualTo(1),
                "a reviewed connection is Connected and sends files");
        }
        finally
        {
            await DisableReviewedTierAsync(frodo);
        }
    }

    // -- peer plumbing works for an unreviewed connection ------------------------------------------

    [Test]
    public async Task FlagOn_UnreviewedConnection_CanStillSendAFile()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(sam, frodo, DrivePermission.Write, "unreviewed-send");

        await EnableReviewedTierAsync(frodo);
        try
        {
            await ClearReviewOnAsync(frodo, sam.Identity);

            var gtid = await SendFileAsync(sam, frodo, drive);

            Assert.That(await CountByGtidAsync(frodo, drive, gtid), Is.EqualTo(1),
                "an unreviewed connection holding a write grant must still be able to send a file");
        }
        finally
        {
            await DisableReviewedTierAsync(frodo);
        }
    }

    [Test]
    public async Task FlagOn_UnreviewedConnection_CanStillSendAReadReceipt()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        // Bidirectional: the receipt lands on Frodo's drive, so Sam needs write there too.
        var drive = await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Write, "unreviewed-receipt",
            bidirectional: true);

        var metadata = SampleMetadataData.Create(fileType: 400, acl: AccessControlList.Connected);
        metadata.AllowDistribution = true;
        var send = await frodo.Drives.Writer.UploadNewMetadata(drive.Alias, metadata,
            transitOptions: new TransitOptions { Recipients = new List<string> { sam.Identity } });
        Assert.That(send.IsSuccessStatusCode, Is.True, $"upload failed: {send.StatusCode}");
        var frodoFile = send.Content!.FileId;
        var gtid = send.Content.GlobalTransitId!.Value;

        await frodo.Sync.DrainOutboxAsync();
        await sam.Sync.ProcessInboxAsync(drive);

        await EnableReviewedTierAsync(frodo);
        try
        {
            await ClearReviewOnAsync(frodo, sam.Identity);

            var samFile = await FindByGtidAsync(sam, drive, gtid);
            var receipt = await sam.Drives.Writer.SendReadReceipt(drive.Alias, new List<Guid> { samFile });
            Assert.That(receipt.IsSuccessStatusCode, Is.True, $"SendReadReceipt failed: {receipt.StatusCode}");

            await sam.Sync.DrainOutboxAsync();
            await frodo.Sync.ProcessInboxAsync(drive);

            var history = await frodo.Drives.Reader.GetTransferHistoryAsync(drive.Alias, frodoFile);
            Assert.That(history.IsSuccessStatusCode, Is.True);
            var item = history.Content!.GetHistoryItem(sam.Identity);
            Assert.That(item, Is.Not.Null);
            Assert.That(item.ReadByRecipientTimestamp, Is.Not.Null,
                "an unreviewed connection must still be able to send a read receipt");
        }
        finally
        {
            await DisableReviewedTierAsync(frodo);
        }
    }

    [Test]
    public async Task FlagOn_UnreviewedConnection_DisconnectIsHonouredByTheOtherSide()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(sam, frodo, DrivePermission.Write, "unreviewed-disconnect");

        await EnableReviewedTierAsync(frodo);
        try
        {
            await ClearReviewOnAsync(frodo, sam.Identity);

            var disconnect = await new V2ConnectionNetworkClient(sam.Identity, sam.Factory).DisconnectAsync(frodo.Identity);
            Assert.That(disconnect.IsSuccessStatusCode, Is.True, $"disconnect failed: {disconnect.StatusCode}");

            // The disconnect notice travels through Sam's outbox.
            await sam.Sync.DrainOutboxAsync();

            var frodoRecordOfSam = await Storage(frodo).GetAsync(sam.Identity);
            Assert.That(frodoRecordOfSam == null || !frodoRecordOfSam.IsConnected(), Is.True,
                "when an unreviewed connection disconnects, the other side must drop the connection too");
        }
        finally
        {
            await DisableReviewedTierAsync(frodo);
        }
    }

    [Test]
    public async Task FlagOn_UnreviewedConnection_CanStillVerifyTheConnection()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(sam, frodo, DrivePermission.Write, "unreviewed-verify");

        await EnableReviewedTierAsync(frodo);
        try
        {
            await ClearReviewOnAsync(frodo, sam.Identity);

            var verify = await new UniversalCircleNetworkApiClient(sam.Identity, sam.Factory).VerifyConnection(frodo.Identity);
            Assert.That(verify.IsSuccessStatusCode, Is.True, $"verify failed: {verify.StatusCode}");
            Assert.That(verify.Content!.IsValid, Is.True,
                "an unreviewed connection is still a connection; verifying it from the other side must succeed");
        }
        finally
        {
            await DisableReviewedTierAsync(frodo);
        }
    }

    // -- content still follows the tier ------------------------------------------------------------

    [Test]
    public async Task ConnectedAclContent_FollowsTheTier_NotTheConnection()
    {
        // The half of the ladder that must not move: an active connection alone does not open content
        // behind a `connected` ACL.  Checked against the evaluator directly, with the caller shaped exactly
        // as transit auth builds it for an unreviewed (Authenticated) and a reviewed (Connected) connection.
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var acl = Host.GetTenantScope(frodo.Identity.DomainName).Resolve<IDriveAclAuthorizationService>();

        var unreviewed = new OdinContext
        {
            Caller = new CallerContext(sam.Identity, null, SecurityGroupType.Authenticated) { HasActiveConnection = true }
        };
        var reviewed = new OdinContext
        {
            Caller = new CallerContext(sam.Identity, null, SecurityGroupType.Connected) { HasActiveConnection = true }
        };

        Assert.That(await acl.CallerHasPermission(AccessControlList.Connected, unreviewed), Is.False,
            "an unreviewed connection must not see content behind a connected ACL");
        Assert.That(await acl.CallerHasPermission(AccessControlList.Connected, reviewed), Is.True,
            "a reviewed connection sees content behind a connected ACL");
    }

    // ---------------------------------------------------------------------------------------------

    // No manual cache reset here: toggling the flag must take effect on its own.
    private static async Task EnableReviewedTierAsync(OwnerSession owner)
    {
        await owner.Admin.UpdateTenantSettingsFlag(TenantConfigFlagNames.UseReviewedSecurityTier, bool.TrueString);
    }

    private static async Task DisableReviewedTierAsync(OwnerSession owner)
    {
        await owner.Admin.UpdateTenantSettingsFlag(TenantConfigFlagNames.UseReviewedSecurityTier, bool.FalseString);
    }

    /// <summary>
    /// Puts <paramref name="peer"/> back to unreviewed on <paramref name="owner"/>'s side.
    /// </summary>
    /// <remarks>
    /// Written to storage rather than through <c>review/clear</c>: the peer-flow setup puts the peer in a
    /// personal circle, which the clear refuses by design.  A storage write bypasses the cache reset the real
    /// clear does, so the cache is reset here.
    /// </remarks>
    private async Task ClearReviewOnAsync(OwnerSession owner, OdinId peer)
    {
        var storage = Storage(owner);
        await storage.UpdateReviewedAtAsync(peer, ConnectionStatus.Connected, null);

        var icr = await storage.GetAsync(peer);
        Assert.That(icr, Is.Not.Null, $"precondition: {owner.Identity} is connected to {peer}");
        Assert.That(icr!.ReviewedAt, Is.Null, $"precondition: {peer} is unreviewed on {owner.Identity}");

        await Host.GetTenantScope(owner.Identity.DomainName).Resolve<OdinContextCache>().ResetAsync();
    }

    private CircleNetworkStorage Storage(OwnerSession owner)
    {
        return Host.GetTenantScope(owner.Identity.DomainName).Resolve<CircleNetworkStorage>();
    }

    private static async Task<Guid> SendFileAsync(OwnerSession sender, OwnerSession recipient, TargetDrive drive)
    {
        var metadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        metadata.AllowDistribution = true;

        var send = await sender.Drives.Writer.UploadNewMetadata(drive.Alias, metadata,
            transitOptions: new TransitOptions { Recipients = new List<string> { recipient.Identity } });
        Assert.That(send.IsSuccessStatusCode, Is.True, $"{sender.Identity} upload failed: {send.StatusCode}");

        var gtid = send.Content!.GlobalTransitId
                   ?? throw new InvalidOperationException("an upload to a recipient must yield a GlobalTransitId");

        await sender.Sync.DrainOutboxAsync();
        await recipient.Sync.ProcessInboxAsync(drive);

        return gtid;
    }

    private static async Task<int> CountByGtidAsync(OwnerSession owner, TargetDrive drive, Guid gtid)
    {
        var q = await owner.Drives.Reader.GetBatchAsync(drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { GlobalTransitId = new[] { gtid } },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10, IncludeMetadataHeader = true },
        });
        Assert.That(q.IsSuccessStatusCode, Is.True, $"{owner.Identity} query failed: {q.StatusCode}");
        return q.Content!.SearchResults.Count();
    }

    private static async Task<Guid> FindByGtidAsync(OwnerSession owner, TargetDrive drive, Guid gtid)
    {
        var q = await owner.Drives.Reader.GetBatchAsync(drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { GlobalTransitId = new[] { gtid } },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10, IncludeMetadataHeader = true },
        });
        Assert.That(q.IsSuccessStatusCode, Is.True);
        var hits = q.Content!.SearchResults.ToList();
        Assert.That(hits.Count, Is.EqualTo(1), $"expected one local file for GTID {gtid} on {owner.Identity}");
        return hits[0].FileId;
    }
}
