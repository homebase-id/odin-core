using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// Port of <c>_V2/Tests/Drive/DriveReaderTests/GetDriveStatusTests</c>. The sender uploads and
/// distributes a file, then queries the V2 drive-status endpoint on the sender side. After the
/// outbox drain, the outbox totals should both report 0. Also checks the bad-drive-id path returns
/// 400. Original parameterized this across the caller-type matrix; the in-process port focuses on
/// the Owner case for the same reason as <c>TransferHistoryTests</c>.
/// </summary>
[TestFixture]
public class DriveStatusTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task CanGetDriveStatus_Owner_OutboxEmptyAfterDrain()
    {
        var (sender, _, drive) = await SetupAndTransferAsync();

        var response = await sender.Drives.Reader.GetDriveStatusAsync(drive.Alias);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var status = response.Content!;
        Assert.That(status.Outbox.TotalItems, Is.EqualTo(0));
        Assert.That(status.Outbox.CheckedOutCount, Is.EqualTo(0));
    }

    [Test]
    public async Task ReceiveBadRequestWhenInvalidDriveSent()
    {
        var (sender, _, _) = await SetupAndTransferAsync();

        var response = await sender.Drives.Reader.GetDriveStatusAsync(Guid.NewGuid());
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    private async Task<(OwnerSession sender, OwnerSession recipient, TargetDrive drive)> SetupAndTransferAsync()
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(sender, recipient, DrivePermission.Write, "drive-status");

        var metadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Anonymous);
        metadata.AllowDistribution = true;

        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var payloads = new List<TestPayloadDefinition> { payload };
        var manifest = new UploadManifest { PayloadDescriptors = payloads.ToPayloadDescriptorList().ToList() };

        var transit = new TransitOptions
        {
            IsTransient = false,
            Recipients = new List<string> { recipient.Identity },
            DisableTransferHistory = false,
            Priority = OutboxPriority.High
        };

        var response = await sender.Drives.Writer.CreateNewUnencryptedFile(drive.Alias, metadata, manifest, payloads, transit);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"upload failed: {response.StatusCode}");

        await sender.Sync.DrainOutboxAsync();
        return (sender, recipient, drive);
    }
}
