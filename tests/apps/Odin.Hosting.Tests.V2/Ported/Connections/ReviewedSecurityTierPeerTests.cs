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
using Odin.Services.Membership.Connections.Requests;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Ported.Connections;

/// <summary>
/// The reviewed security tier (<see cref="TenantConfigFlagNames.UseReviewedSecurityTier"/>).
/// </summary>
/// <remarks>
/// Frodo and Sam are connected.  The rules:
/// <list type="number">
/// <item>With the flag off on either side, nothing changes -- a dark launch never reaches anyone else.</item>
/// <item>With the flag on on both sides, an unreviewed connection can still message: send files, read
/// receipts, disconnect, verify, and introduce.</item>
/// <item>With the flag on on both sides, an unreviewed connection cannot see content marked <c>connected</c>.</item>
/// </list>
/// A connection is admitted at Connected with <see cref="CallerContext.IsReviewed"/> set from the review stamp
/// and <see cref="CallerContext.CallerUsesReviewedTier"/> set from the caller's header; content evaluation applies
/// <see cref="ReviewedSecurityTier.EffectiveLevel"/>.  Introductions are decided by
/// <see cref="TenantConfigFlagNames.DisableAllowIntroductions"/> instead, whatever the tier.
/// <para>
/// Not covered here: peer file updates, the peer app-notification token, and the verification-hash sync push.
/// </para>
/// </remarks>
[TestFixture]
public class ReviewedSecurityTierPeerTests : V2Fixture
{
    /// <summary>These tests drive introductions that are expected to fail delivery.</summary>
    protected override IReadOnlyCollection<string> ToleratedErrorLogSubstrings =>
        [OutboxDeliveryFailureLogged];

    private const int ConnectedContentFileType = 7021;

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    // -- flag off: nothing changes ----------------------------------------------------------------

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
    public async Task FlagOff_UnreviewedConnection_CanSeeConnectedContent()
    {
        var (frodo, sam, drive, fileId) = await SetupConnectedContentAsync("content-flag-off");

        await ClearReviewOnAsync(frodo, sam.Identity);

        await AssertSamSeesContentAsync(frodo, sam, drive, fileId, "with the flag off");
    }

    [Test]
    public async Task FlagOff_UnreviewedConnection_CanStillIntroduce()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await ConnectOwnersAsync(frodo, sam);

        await ClearReviewOnAsync(sam, frodo.Identity);

        var status = await PreflightIntroductionToAsync(frodo, sam);
        Assert.That(status.AllowsIntroductions, Is.True, "with the flag off an unreviewed connection introduces as before");
        Assert.That(status.Status, Is.EqualTo(IntroductionPreflightStatus.Ready), $"detail={status.Detail}");
    }

    // -- flag on one side only: the other identity is not affected ----------------------------------

    [Test]
    public async Task OnlyHostFlagOn_UnreviewedConnection_CanSeeConnectedContent()
    {
        var (frodo, sam, drive, fileId) = await SetupConnectedContentAsync("content-host-only");

        await EnableReviewedTierAsync(frodo);
        try
        {
            await ClearReviewOnAsync(frodo, sam.Identity);

            await AssertSamSeesContentAsync(frodo, sam, drive, fileId,
                "Sam does not use the tier, so Frodo turning it on must not change what Sam sees");
        }
        finally
        {
            await DisableReviewedTierAsync(frodo);
        }
    }

    [Test]
    public async Task OnlyCallerFlagOn_UnreviewedConnection_CanSeeConnectedContent()
    {
        var (frodo, sam, drive, fileId) = await SetupConnectedContentAsync("content-caller-only");

        await EnableReviewedTierAsync(sam);
        try
        {
            await ClearReviewOnAsync(frodo, sam.Identity);

            await AssertSamSeesContentAsync(frodo, sam, drive, fileId,
                "Frodo does not use the tier, so Sam turning it on must not change what Sam sees");
        }
        finally
        {
            await DisableReviewedTierAsync(sam);
        }
    }

    [Test]
    public async Task OnlyRecipientFlagOn_UnreviewedConnection_CanIntroduce()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await ConnectOwnersAsync(frodo, sam);

        await EnableReviewedTierAsync(sam);
        try
        {
            await ClearReviewOnAsync(sam, frodo.Identity);

            var status = await PreflightIntroductionToAsync(frodo, sam);
            Assert.That(status.AllowsIntroductions, Is.True,
                "Frodo does not use the tier, so Sam turning it on must not stop Frodo introducing");
        }
        finally
        {
            await DisableReviewedTierAsync(sam);
        }
    }

    [Test]
    public async Task OnlyIntroducerFlagOn_UnreviewedConnection_CanIntroduce()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await ConnectOwnersAsync(frodo, sam);

        await EnableReviewedTierAsync(frodo);
        try
        {
            await ClearReviewOnAsync(sam, frodo.Identity);

            var status = await PreflightIntroductionToAsync(frodo, sam);
            Assert.That(status.AllowsIntroductions, Is.True,
                "Sam does not use the tier, so Frodo turning it on must not stop Frodo introducing");
        }
        finally
        {
            await DisableReviewedTierAsync(frodo);
        }
    }

    // -- flag on both sides: reviewed connections ----------------------------------------------------

    [Test]
    public async Task BothFlagsOn_ReviewedConnection_CanSendAFile()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(sam, frodo, DrivePermission.Write, "reviewed");

        await EnableReviewedTierAsync(frodo, sam);
        try
        {
            var gtid = await SendFileAsync(sam, frodo, drive);

            Assert.That(await CountByGtidAsync(frodo, drive, gtid), Is.EqualTo(1),
                "a reviewed connection sends files");
        }
        finally
        {
            await DisableReviewedTierAsync(frodo, sam);
        }
    }

    [Test]
    public async Task BothFlagsOn_ReviewedConnection_CanSeeConnectedContent()
    {
        var (frodo, sam, drive, fileId) = await SetupConnectedContentAsync("content-reviewed");

        await EnableReviewedTierAsync(frodo, sam);
        try
        {
            await AssertSamSeesContentAsync(frodo, sam, drive, fileId, "a reviewed connection");
        }
        finally
        {
            await DisableReviewedTierAsync(frodo, sam);
        }
    }

    [Test]
    public async Task BothFlagsOn_ReviewedConnection_CanIntroduce()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await ConnectOwnersAsync(frodo, sam);

        await EnableReviewedTierAsync(frodo, sam);
        try
        {
            var status = await PreflightIntroductionToAsync(frodo, sam);
            Assert.That(status.AllowsIntroductions, Is.True, "a reviewed connection may introduce");
            Assert.That(status.Status, Is.EqualTo(IntroductionPreflightStatus.Ready), $"detail={status.Detail}");
        }
        finally
        {
            await DisableReviewedTierAsync(frodo, sam);
        }
    }

    // -- flag on both sides: an unreviewed connection can still message ------------------------------

    [Test]
    public async Task BothFlagsOn_UnreviewedConnection_CanStillSendAFile()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(sam, frodo, DrivePermission.Write, "unreviewed-send");

        await EnableReviewedTierAsync(frodo, sam);
        try
        {
            await ClearReviewOnAsync(frodo, sam.Identity);

            var gtid = await SendFileAsync(sam, frodo, drive);

            Assert.That(await CountByGtidAsync(frodo, drive, gtid), Is.EqualTo(1),
                "an unreviewed connection holding a write grant must still be able to send a file");
        }
        finally
        {
            await DisableReviewedTierAsync(frodo, sam);
        }
    }

    [Test]
    public async Task BothFlagsOn_UnreviewedConnection_CanStillSendAReadReceipt()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        // The receipt lands on Frodo's drive, so Sam needs write there too.
        var drive = await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Write, "unreviewed-receipt",
            recipientPermissionOnSenderDrive: DrivePermission.Write);

        var metadata = SampleMetadataData.Create(fileType: 400, acl: AccessControlList.Connected);
        metadata.AllowDistribution = true;
        var send = await frodo.Drives.Writer.UploadNewMetadata(drive.Alias, metadata,
            transitOptions: new TransitOptions { Recipients = new List<string> { sam.Identity } });
        Assert.That(send.IsSuccessStatusCode, Is.True, $"upload failed: {send.StatusCode}");
        var frodoFile = send.Content!.FileId;
        var gtid = send.Content.GlobalTransitId!.Value;

        await frodo.Sync.DrainOutboxAsync();
        await sam.Sync.ProcessInboxAsync(drive);

        await EnableReviewedTierAsync(frodo, sam);
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
            await DisableReviewedTierAsync(frodo, sam);
        }
    }

    [Test]
    public async Task BothFlagsOn_UnreviewedConnection_DisconnectIsHonouredByTheOtherSide()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(sam, frodo, DrivePermission.Write, "unreviewed-disconnect");

        await EnableReviewedTierAsync(frodo, sam);
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
            await DisableReviewedTierAsync(frodo, sam);
        }
    }

    [Test]
    public async Task BothFlagsOn_UnreviewedConnection_CanStillVerifyTheConnection()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(sam, frodo, DrivePermission.Write, "unreviewed-verify");

        await EnableReviewedTierAsync(frodo, sam);
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
            await DisableReviewedTierAsync(frodo, sam);
        }
    }

    // -- flag on both sides: an unreviewed connection is restricted ----------------------------------

    [Test]
    public async Task BothFlagsOn_UnreviewedConnection_CannotSeeConnectedContent()
    {
        var (frodo, sam, drive, fileId) = await SetupConnectedContentAsync("content-unreviewed");

        await EnableReviewedTierAsync(frodo, sam);
        try
        {
            await ClearReviewOnAsync(frodo, sam.Identity);

            await AssertSamCannotSeeContentAsync(frodo, sam, drive, fileId);
        }
        finally
        {
            await DisableReviewedTierAsync(frodo, sam);
        }
    }

    [Test]
    public async Task BothFlagsOn_UnreviewedConnection_CanStillIntroduce()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await ConnectOwnersAsync(frodo, sam);

        await EnableReviewedTierAsync(frodo, sam);
        try
        {
            await ClearReviewOnAsync(sam, frodo.Identity);

            var status = await PreflightIntroductionToAsync(frodo, sam);
            Assert.That(status.AllowsIntroductions, Is.True,
                "introductions are decided by DisableAllowIntroductions, not by the reviewed tier");
            Assert.That(status.Status, Is.EqualTo(IntroductionPreflightStatus.Ready), $"detail={status.Detail}");
            Assert.That(status.IsCallerConnected, Is.True);
        }
        finally
        {
            await DisableReviewedTierAsync(frodo, sam);
        }
    }

    [Test]
    public async Task BothFlagsOn_ThenCallerTurnsItOff_ContentIsVisibleAgainWithoutACacheReset()
    {
        // The caller's header is part of the transit context cache key, so turning the tier off on Sam's side
        // must take effect on Frodo's side at once -- no cached context built under the old header.
        var (frodo, sam, drive, fileId) = await SetupConnectedContentAsync("content-toggle");

        await EnableReviewedTierAsync(frodo, sam);
        try
        {
            await ClearReviewOnAsync(frodo, sam.Identity);
            await AssertSamCannotSeeContentAsync(frodo, sam, drive, fileId);

            await DisableReviewedTierAsync(sam);

            await AssertSamSeesContentAsync(frodo, sam, drive, fileId, "after Sam turns the tier off");
        }
        finally
        {
            await DisableReviewedTierAsync(frodo, sam);
        }
    }

    // ---------------------------------------------------------------------------------------------

    private static async Task EnableReviewedTierAsync(params OwnerSession[] owners)
    {
        foreach (var owner in owners)
        {
            await owner.Admin.UpdateTenantSettingsFlag(TenantConfigFlagNames.UseReviewedSecurityTier, bool.TrueString);
        }
    }

    private static async Task DisableReviewedTierAsync(params OwnerSession[] owners)
    {
        foreach (var owner in owners)
        {
            await owner.Admin.UpdateTenantSettingsFlag(TenantConfigFlagNames.UseReviewedSecurityTier, bool.FalseString);
        }
    }

    private static async Task ConnectOwnersAsync(OwnerSession sender, OwnerSession recipient)
    {
        var send = await sender.Connections.SendConnectionRequest(recipient.Identity);
        Assert.That(send.IsSuccessStatusCode, Is.True, $"send to {recipient.Identity} failed: {send.StatusCode}");

        var accept = await recipient.Connections.AcceptConnectionRequest(sender.Identity);
        Assert.That(accept.IsSuccessStatusCode, Is.True, $"accept on {recipient.Identity} failed: {accept.StatusCode}");
    }

    private static async Task<RecipientPreflightStatus> PreflightIntroductionToAsync(OwnerSession introducer,
        OwnerSession recipient)
    {
        var response = await introducer.Connections.PreflightIntroductionsAsync(new IntroductionGroup
        {
            Message = "preflight",
            Recipients = [recipient.Identity]
        });
        Assert.That(response.IsSuccessStatusCode, Is.True, $"preflight failed: {response.StatusCode}");
        return response.Content!.Recipients.Single(r => r.Recipient == recipient.Identity.DomainName);
    }

    /// <summary>
    /// Frodo hosts a drive holding one file marked <c>connected</c>; Sam is connected with Read on it.
    /// </summary>
    private async Task<(OwnerSession frodo, OwnerSession sam, TargetDrive drive, Guid fileId)> SetupConnectedContentAsync(
        string label)
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        // Sam is the sender, so Frodo's circle grants Sam Read on Frodo's drive.
        var drive = await PeerFlow.CreatePeerDriveAsync(sam, frodo, DrivePermission.Read, label, allowAnonymousReads: false);

        var metadata = SampleMetadataData.Create(fileType: ConnectedContentFileType, acl: AccessControlList.Connected);
        var upload = await frodo.Drives.Writer.UploadNewMetadata(drive.Alias, metadata);
        Assert.That(upload.IsSuccessStatusCode, Is.True, $"frodo upload failed: {upload.StatusCode}");

        return (frodo, sam, drive, upload.Content!.FileId);
    }

    private static async Task AssertSamSeesContentAsync(OwnerSession frodo, OwnerSession sam, TargetDrive drive,
        Guid fileId, string because)
    {
        Assert.That(await PeerQueryCountAsync(sam, frodo, drive), Is.EqualTo(1), $"{because}: Sam sees connected content");
        var header = await sam.Drives.Peer.GetFileHeaderAsync(frodo.Identity, drive.Alias, fileId);
        Assert.That(header.IsSuccessStatusCode, Is.True, $"{because}: Sam reads the header; got {header.StatusCode}");
    }

    private static async Task AssertSamCannotSeeContentAsync(OwnerSession frodo, OwnerSession sam, TargetDrive drive,
        Guid fileId)
    {
        Assert.That(await PeerQueryCountAsync(sam, frodo, drive), Is.EqualTo(0),
            "an unreviewed connection must not see connected content in a query");
        var header = await sam.Drives.Peer.GetFileHeaderAsync(frodo.Identity, drive.Alias, fileId);
        Assert.That(header.IsSuccessStatusCode, Is.False,
            $"an unreviewed connection must not read a connected file's header; got {header.StatusCode}");
    }

    private static async Task<int> PeerQueryCountAsync(OwnerSession reader, OwnerSession host, TargetDrive drive)
    {
        var response = await reader.Drives.Peer.QueryBatchAsync(host.Identity, drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { FileType = new[] { ConnectedContentFileType } },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10, IncludeMetadataHeader = true }
        });
        Assert.That(response.IsSuccessStatusCode, Is.True, $"peer query failed: {response.StatusCode}");
        return response.Content!.SearchResults.Count();
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
