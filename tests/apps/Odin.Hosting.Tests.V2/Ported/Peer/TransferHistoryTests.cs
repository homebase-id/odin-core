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
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// Port of <c>_V2/Tests/Drive/DriveReaderTests/GetTransferHistoryTests</c>. After distributing a
/// file from Frodo to Sam, the sender (Frodo) queries the transfer history for the file and
/// expects to see each recipient's delivery status (Delivered) and a null read-receipt timestamp
/// (no read receipt has been sent). The original parameterized this across the full caller-type
/// matrix; the in-process port focuses on the Owner case — App/Guest variants from the original
/// are deferred (the CDN row goes with Phase 4, App/Guest go alongside Connections-style flows).
/// </summary>
[TestFixture]
public class TransferHistoryTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task GetTransferHistory_Owner_DeliveredToRecipient()
    {
        var (sender, recipient, drive, uploadFileId) = await SetupAndTransferAsync();

        var response = await sender.Drives.Reader.GetTransferHistoryAsync(drive.Alias, uploadFileId);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var history = response.Content!;
        var item = history.GetHistoryItem(recipient.Identity);
        Assert.That(item, Is.Not.Null);
        Assert.That(item!.LatestTransferStatus, Is.EqualTo(LatestTransferStatus.Delivered),
            $"actual {item.LatestTransferStatus}");
    }

    [Test]
    public async Task GetTransferHistory_IsReadByRecipient_ReturnsNullableTimestamp()
    {
        var (sender, recipient, drive, uploadFileId) = await SetupAndTransferAsync();

        var response = await sender.Drives.Reader.GetTransferHistoryAsync(drive.Alias, uploadFileId);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var item = response.Content!.GetHistoryItem(recipient.Identity);
        Assert.That(item, Is.Not.Null);
        Assert.That(item!.LatestTransferStatus, Is.EqualTo(LatestTransferStatus.Delivered));
        Assert.That(item.ReadByRecipientTimestamp, Is.Null,
            "V2 IsReadByRecipient should be null for unread items (not false)");
    }

    private async Task<(OwnerSession sender, OwnerSession recipient, TargetDrive drive, Guid fileId)> SetupAndTransferAsync()
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);
        var drive = await PeerFlow.CreatePeerDriveAsync(sender, recipient, DrivePermission.Write, "transfer-history");

        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.AccessControlList = AccessControlList.Authenticated;
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
        await recipient.Sync.ProcessInboxAsync(drive);

        return (sender, recipient, drive, response.Content!.FileId);
    }
}
