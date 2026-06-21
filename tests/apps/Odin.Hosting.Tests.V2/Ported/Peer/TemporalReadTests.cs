using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// End-to-end coverage for the Conditional-Temporal-Read ("emergency / time-boxed") read API.
/// Frodo owns a sensitive drive with a configured max-age window and grants Sam
/// <see cref="DrivePermission.ConditionalTemporalRead"/> through a circle. Sam can read recent files
/// only via the dedicated temporal endpoints; the normal read endpoints reject the temporal-only grant.
/// </summary>
[TestFixture]
public class TemporalReadTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    private const int WindowSeconds = 4;

    [Test]
    public async Task TemporalRead_ClampsToWindow_VerifyReportsAccess_AndNormalReadIsRejected()
    {
        // Frodo owns the sensitive drive; Sam is the family member who reads it in an emergency.
        var sam = await LoginAsOwner(Identities.Sam);
        var frodo = await LoginAsOwner(Identities.Frodo);

        var drive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(drive, "location", allowAnonymousReads: false,
            attributes: new Dictionary<string, string> { [TemporalRead.MaxAgeAttributeKey] = WindowSeconds.ToString() });

        // Grant Sam ConditionalTemporalRead on Frodo's drive via a circle.
        await PeerFlow.ConnectAsync(sam, frodo, drive, DrivePermission.ConditionalTemporalRead);

        // Upload an "old" file, wait past the window, then a "fresh" file.
        var oldFileId = await UploadLocalAsync(frodo, drive, "old location");
        await Task.Delay((WindowSeconds + 3) * 1000);
        var beforeFresh = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var freshFileId = await UploadLocalAsync(frodo, drive, "fresh location");

        // verify: Sam has temporal access; the reported window matches the drive ceiling, and the newest-file
        // timestamp reflects the most recent upload (unclamped — it's the "last data update" signal).
        var verify = await sam.Drives.Peer.VerifyTemporalAccessAsync(frodo.Identity, drive.Alias);
        Assert.That(verify.IsSuccessStatusCode, Is.True, $"verify failed: {verify.StatusCode}");
        Assert.That(verify.Content!.HasAccess, Is.True);
        Assert.That(verify.Content.WindowSeconds, Is.EqualTo(WindowSeconds));
        Assert.That(verify.Content.NewestFileModified.milliseconds, Is.GreaterThanOrEqualTo(beforeFresh - 2000),
            "newest-file timestamp should reflect the most recent upload");

        // Temporal read of the fresh file succeeds...
        var fresh = await sam.Drives.Peer.TemporalGetFileHeaderAsync(frodo.Identity, drive.Alias, freshFileId);
        Assert.That(fresh.IsSuccessStatusCode, Is.True, $"expected fresh file readable; got {fresh.StatusCode}");
        Assert.That(fresh.Content, Is.Not.Null);

        // ...but the out-of-window old file is reported as not found.
        var old = await sam.Drives.Peer.TemporalGetFileHeaderAsync(frodo.Identity, drive.Alias, oldFileId);
        Assert.That(old.StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
            $"out-of-window file must be 404; got {old.StatusCode}");

        // Fail-safe: the NORMAL (non-temporal) read endpoint rejects the temporal-only grant entirely,
        // even for an in-window file.
        var normal = await sam.Drives.Peer.GetFileHeaderAsync(frodo.Identity, drive.Alias, freshFileId);
        Assert.That(normal.IsSuccessStatusCode, Is.False,
            $"temporal-only caller must be rejected on the normal read endpoint; got {normal.StatusCode}");
    }

    private static async Task<Guid> UploadLocalAsync(OwnerSession owner, TargetDrive drive, string content)
    {
        var metadata = SampleMetadataData.Create(fileType: 9001, acl: AccessControlList.Connected, allowDistribution: false);
        metadata.AppData.Content = content;
        var response = await owner.Drives.Writer.UploadNewMetadata(drive.Alias, metadata);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"local upload failed: {response.StatusCode}");
        return response.Content!.FileId;
    }
}
