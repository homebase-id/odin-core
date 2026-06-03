using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// Port of <c>_V2/Tests/Drive/PeerQueryTests/GetFileExistsOnPeerTestsV2</c>. Exercises the V2 peer
/// "file-exists" endpoints (by UniqueId and by GlobalTransitId) across the tier rules:
/// <list type="bullet">
///   <item>Read on drive → VersionTag returned.</item>
///   <item>Write only, caller IS OriginalAuthor → VersionTag returned.</item>
///   <item>Write only, caller is NOT OriginalAuthor → VersionTag null.</item>
///   <item>No drive grant → non-success.</item>
///   <item>File missing → Exists=false, VersionTag=null.</item>
/// </list>
/// </summary>
[TestFixture]
public class FileExistsTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task ByUid_ReadCaller_FilePresent_ReturnsExistsTrueAndVersionTag()
    {
        var (sender, recipient, drive) = await SetupConnectedAsync(DrivePermission.Read | DrivePermission.Write);

        var uniqueId = Guid.NewGuid();
        var gtid = await UploadAndDistributeAsync(sender, recipient, drive, uniqueId);
        var recipientVersionTag = await RecipientVersionTagAsync(recipient, drive, gtid, uniqueId);

        var response = await sender.Drives.Peer.GetFileExistsByUidAsync(recipient.Identity, drive.Alias, uniqueId);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"expected 200; got {response.StatusCode}");
        Assert.That(response.Content!.Exists, Is.True);
        Assert.That(response.Content.VersionTag, Is.EqualTo(recipientVersionTag),
            "Read caller is entitled to the VersionTag");
    }

    [Test]
    public async Task ByGtid_ReadCaller_FilePresent_ReturnsExistsTrueAndVersionTag()
    {
        var (sender, recipient, drive) = await SetupConnectedAsync(DrivePermission.Read | DrivePermission.Write);

        var gtid = await UploadAndDistributeAsync(sender, recipient, drive, Guid.NewGuid());
        var recipientVersionTag = await RecipientVersionTagAsync(recipient, drive, gtid, expectedUniqueId: null);

        var response = await sender.Drives.Peer.GetFileExistsByGtidAsync(recipient.Identity, drive.Alias, gtid);
        Assert.That(response.IsSuccessStatusCode, Is.True);
        Assert.That(response.Content!.Exists, Is.True);
        Assert.That(response.Content.VersionTag, Is.EqualTo(recipientVersionTag));
    }

    [Test]
    public async Task ByUid_ReadCaller_FileMissing_ReturnsExistsFalse()
    {
        var (sender, recipient, drive) = await SetupConnectedAsync(DrivePermission.Read | DrivePermission.Write);

        var response = await sender.Drives.Peer.GetFileExistsByUidAsync(recipient.Identity, drive.Alias, Guid.NewGuid());
        Assert.That(response.IsSuccessStatusCode, Is.True);
        Assert.That(response.Content!.Exists, Is.False);
        Assert.That(response.Content.VersionTag, Is.Null);
    }

    [Test]
    public async Task ByUid_WriteOnlyCaller_Author_ReturnsExistsTrueAndVersionTag()
    {
        var (sender, recipient, drive) = await SetupConnectedAsync(DrivePermission.Write);

        var uniqueId = Guid.NewGuid();
        var gtid = await UploadAndDistributeAsync(sender, recipient, drive, uniqueId);
        var recipientVersionTag = await RecipientVersionTagAsync(recipient, drive, gtid, uniqueId);

        // Sender uploaded → sender IS OriginalAuthor on the recipient's copy.
        var response = await sender.Drives.Peer.GetFileExistsByUidAsync(recipient.Identity, drive.Alias, uniqueId);
        Assert.That(response.IsSuccessStatusCode, Is.True);
        Assert.That(response.Content!.Exists, Is.True);
        Assert.That(response.Content.VersionTag, Is.EqualTo(recipientVersionTag),
            "Write-only caller IS the OriginalAuthor; should still receive the VersionTag");
    }

    [Test]
    public async Task ByUid_WriteOnlyCaller_NotAuthor_ReturnsExistsTrueButNullVersionTag()
    {
        // Recipient (Sam) uploads the file LOCALLY → Sam is OriginalAuthor.
        // Caller (Frodo) has Write-only on Sam's drive but is not the author.
        var (sender, recipient, drive) = await SetupConnectedAsync(DrivePermission.Write);

        var uniqueId = Guid.NewGuid();
        var metadata = SampleMetadataData.Create(fileType: 9001, acl: AccessControlList.Connected, allowDistribution: false);
        metadata.AppData.UniqueId = uniqueId;
        var local = await recipient.Drives.Writer.UploadNewMetadata(drive.Alias, metadata);
        Assert.That(local.IsSuccessStatusCode, Is.True, $"local upload failed: {local.StatusCode}");

        var response = await sender.Drives.Peer.GetFileExistsByUidAsync(recipient.Identity, drive.Alias, uniqueId);
        Assert.That(response.IsSuccessStatusCode, Is.True);
        Assert.That(response.Content!.Exists, Is.True);
        Assert.That(response.Content.VersionTag, Is.Null,
            "Write-only caller who is not the OriginalAuthor must not receive the VersionTag");
    }

    [Test]
    public async Task ByUid_CallerWithNoDrivePermission_ReturnsNonSuccess()
    {
        // Connect sender and recipient, but the circle grants no permission on the queried drive.
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        var grantedDrive = TargetDrive.NewTargetDrive();
        var queriedDrive = TargetDrive.NewTargetDrive();

        // allowAnonymousReads=false on the queried drive: otherwise every connected peer
        // gets implicit Read via the anonymous tier, which is the exact case this test
        // is trying to prove is rejected.
        await sender.Admin.CreateDrive(grantedDrive, "granted", allowAnonymousReads: false);
        await recipient.Admin.CreateDrive(grantedDrive, "granted", allowAnonymousReads: false);
        await recipient.Admin.CreateDrive(queriedDrive, "queried", allowAnonymousReads: false);

        await PeerFlow.ConnectAsync(sender, recipient, grantedDrive, DrivePermission.Write);

        var response = await sender.Drives.Peer.GetFileExistsByUidAsync(recipient.Identity, queriedDrive.Alias, Guid.NewGuid());
        Assert.That(response.IsSuccessStatusCode, Is.False,
            $"caller has no permission on the queried drive; expected non-success, got {response.StatusCode}");
    }

    // -----------------------------------------------------------------------------------------
    // helpers
    // -----------------------------------------------------------------------------------------

    private async Task<(OwnerSession sender, OwnerSession recipient, TargetDrive drive)> SetupConnectedAsync(DrivePermission granted)
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);
        // allowAnonymousReads=false: this fixture exercises the file-exists *tier* rules
        // (Read vs. Write-only-but-author vs. Write-only-not-author). An anonymous-read drive
        // grants every connected caller an implicit Read, which short-circuits the tier and
        // surfaces VersionTag to callers who shouldn't see it.
        var drive = await PeerFlow.CreatePeerDriveAsync(sender, recipient, granted, "file-exists test",
            allowAnonymousReads: false);
        return (sender, recipient, drive);
    }

    private static async Task<Guid> UploadAndDistributeAsync(
        OwnerSession sender, OwnerSession recipient, TargetDrive drive, Guid uniqueId)
    {
        var metadata = SampleMetadataData.Create(fileType: 9001, acl: AccessControlList.Connected, allowDistribution: true);
        metadata.AppData.UniqueId = uniqueId;

        var transit = new TransitOptions
        {
            IsTransient = false,
            Recipients = new List<string> { recipient.Identity },
            DisableTransferHistory = false,
            Priority = OutboxPriority.High
        };

        var response = await sender.Drives.Writer.UploadNewMetadata(drive.Alias, metadata, transit);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"upload failed: {response.StatusCode}");
        var gtid = response.Content!.GlobalTransitId
            ?? throw new InvalidOperationException("distribution upload must yield a GlobalTransitId");

        await sender.Sync.DrainOutboxAsync();
        await recipient.Sync.ProcessInboxAsync(drive);
        return gtid;
    }

    private static async Task<Guid> RecipientVersionTagAsync(
        OwnerSession recipient, TargetDrive drive, Guid gtid, Guid? expectedUniqueId)
    {
        var q = await recipient.Drives.Reader.GetBatchAsync(drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { GlobalTransitId = new[] { gtid } },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest
            {
                MaxRecords = 10,
                IncludeMetadataHeader = true
            }
        });
        Assert.That(q.IsSuccessStatusCode, Is.True);
        var header = q.Content!.SearchResults.SingleOrDefault();
        Assert.That(header, Is.Not.Null, "recipient should have a copy after distribution");
        if (expectedUniqueId.HasValue)
        {
            Assert.That(header!.FileMetadata.AppData.UniqueId, Is.EqualTo(expectedUniqueId.Value));
        }
        return header!.FileMetadata.VersionTag;
    }
}
