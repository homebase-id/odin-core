#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Storage;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Hosting;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Hosting.Tests.V2.Ported.Peer;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Contacts;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Odin.Services.Profile;

namespace Odin.Hosting.Tests.V2.Storage;

/// <summary>
/// Every path that adds payload bytes refuses them once the identity is on a full plan (docs/hosting-plans.md), and
/// a peer's file waits in the sender's outbox, leaving nothing on the full recipient, until set-plan gives it room.
/// TenantQuotaTests covers the same guard for a hand-set OutOfQuota and the owner's own uploads.
/// </summary>
[TestFixture]
public class PlanQuotaPathTests : V2Fixture
{
    private const string FullPlan = "full";
    private const string DevPlan = "dev-domain";

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    protected override IReadOnlyDictionary<string, string?> ConfigOverrides =>
        new Dictionary<string, string?>(TestAdminApi.Config())
        {
            ["Registry:Plans:0:Id"] = "free",
            ["Registry:Plans:0:Default"] = "true",
            // The test identities' own plan (DevEnvironmentSetup), unlimited so only the identity moved to "full" is full
            ["Registry:Plans:1:Id"] = DevPlan,
            ["Registry:Plans:2:Id"] = FullPlan,
            ["Registry:Plans:2:QuotaBytes"] = "0",
        };

    /// <summary>The plan lives in the identity registry, which the per-test reset does not restore</summary>
    [TearDown]
    public async Task RestorePlans()
    {
        foreach (var identity in HostIdentities)
        {
            await SetPlanAsync(identity, DevPlan);
        }
    }

    //
    // Local writes other than the owner's own uploads
    //

    [Test]
    public async Task AnAppsPayloadIsRefusedButItsHeaderIsNot()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = await PlanQuotaTests.CreateDriveAsync(owner);
        var app = await AppSession.SetupAsync(owner, drive, DrivePermission.ReadWrite);
        await SetPlanAsync(Identities.Frodo, FullPlan);

        var payloads = new List<TestPayloadDefinition> { SamplePayloadDefinitions.GetPayloadDefinition1() };
        var withPayload = await app.V1.Drive.UploadNewFile(drive, SampleMetadataData.CreateWithContent(200, "x"),
            ManifestFor(payloads), payloads);
        Assert.That(withPayload.StatusCode, Is.EqualTo(HttpStatusCode.InsufficientStorage));

        var headerOnly = await app.V1.Drive.UploadNewMetadata(drive, SampleMetadataData.Create(fileType: 100));
        Assert.That(headerOnly.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task ACommentsPayloadIsRefusedButTheCommentIsNot()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = await PlanQuotaTests.CreateDriveAsync(owner);
        var post = await owner.V1.Drive.UploadNewMetadata(drive, SampleMetadataData.Create(fileType: 333));
        Assert.That(post.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await SetPlanAsync(Identities.Frodo, FullPlan);

        var comment = new UploadFileMetadata
        {
            ReferencedFile = post.Content!.GlobalTransitIdFileIdentifier,
            AppData = new UploadAppFileMetaData { FileType = 10101, Content = "a comment" }
        };
        var payloads = new List<TestPayloadDefinition> { SamplePayloadDefinitions.GetPayloadDefinition1() };
        var withPayload = await owner.V1.Drive.UploadNewFile(drive, comment, ManifestFor(payloads), payloads,
            fileSystemType: FileSystemType.Comment);
        Assert.That(withPayload.StatusCode, Is.EqualTo(HttpStatusCode.InsufficientStorage));

        var headerOnly = await owner.V1.Drive.UploadNewMetadata(drive, comment, FileSystemType.Comment);
        Assert.That(headerOnly.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task AProfilePhotoIsRefused()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        await SetPlanAsync(Identities.Frodo, FullPlan);

        var response = await new V2ProfileClient(owner.Identity, owner.Factory).SetPhotoAttributeAsync(new SetPhotoAttributeRequest
        {
            Visibility = ProfileAttributeVisibility.Anonymous,
            ContentType = "image/webp",
            Content = Enumerable.Repeat((byte)0xAB, 128).ToArray(),
            Thumbnails = []
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InsufficientStorage));
    }

    [Test]
    public async Task AContactImageIsRefusedButTheContactIsNot()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var contacts = new V2ContactsClient(owner.Identity, owner.Factory);
        await SetPlanAsync(Identities.Frodo, FullPlan);

        var create = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Sam" } }
        });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the contact itself has no payload");

        // Refused before the content is read, so the bytes need not be a real encrypted image
        var image = await contacts.SetImageAsync(create.Content!.UniqueId, new SetContactImageRequest
        {
            VersionTag = create.Content.VersionTag,
            ContentType = "image/jpeg",
            Iv = ByteArrayUtil.GetRndByteArray(16),
            Content = ByteArrayUtil.GetRndByteArray(64),
            Thumbnails = []
        });
        Assert.That(image.StatusCode, Is.EqualTo(HttpStatusCode.InsufficientStorage));
    }

    //
    // Peers
    //

    [Test]
    public async Task APeersFileWaitsLeavingNothingOnTheFullRecipientUntilSetPlanGivesItRoom()
    {
        var sam = await LoginAsOwner(Identities.Sam);
        var frodo = await LoginAsOwner(Identities.Frodo);
        var drive = await PeerFlow.CreatePeerDriveAsync(sam, frodo, DrivePermission.Write);
        var stagedBefore = StagedUploadFiles(frodo, drive);
        await SetPlanAsync(Identities.Frodo, FullPlan);

        var payloads = new List<TestPayloadDefinition> { SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1() };
        var send = await sam.V1.Drive.UploadNewFile(drive, SharedMetadata("a file with an attachment"), ManifestFor(payloads), payloads,
            new TransitOptions { Recipients = [frodo.Identity] });
        Assert.That(send.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        await sam.Sync.DrainOutboxAsync();
        Assert.That(await sam.Sync.IsOutboxEmptyAsync(drive), Is.False, "the refused file stays in the sender's outbox");
        var waiting = (await sam.V1.Drive.GetTransferHistory(send.Content!.File)).Content!.GetHistoryItem(frodo.Identity);
        Assert.That(waiting.LatestTransferStatus, Is.EqualTo(LatestTransferStatus.RecipientIdentityOutOfStorage),
            "the sender knows why: the recipient is out of storage");
        Assert.That(waiting.IsInOutbox, Is.True, "and that it is still trying");
        var summary = (await sam.V1.Drive.GetFileHeader(send.Content.File)).Content!.ServerMetadata.TransferHistory.Summary;
        Assert.That(summary.TotalFailed, Is.Zero, "waiting for room is not a failure");
        Assert.That(summary.TotalInOutbox, Is.EqualTo(1));

        var inbox = await frodo.Sync.ProcessInboxAsync(drive);
        Assert.That(inbox.TotalItems, Is.Zero, "nothing reached the full recipient's inbox");
        Assert.That((await frodo.V1.Drive.QueryByGlobalTransitId(send.Content!.GlobalTransitIdFileIdentifier)).Content!.SearchResults,
            Is.Empty, "no header without its payload");
        Assert.That(StagedUploadFiles(frodo, drive), Is.EquivalentTo(stagedBefore), "and nothing half-received left on disk");

        await SetPlanAsync(Identities.Frodo, DevPlan);
        await DeliverDeferredAsync(sam, frodo, drive);

        var received = (await frodo.V1.Drive.QueryByGlobalTransitId(send.Content.GlobalTransitIdFileIdentifier)).Content!.SearchResults;
        Assert.That(received.Count(), Is.EqualTo(1), "delivered once, not twice");
        Assert.That(received.Single().FileMetadata.Payloads.Single().Key, Is.EqualTo(payloads[0].Key), "with its payload");
    }

    [Test]
    public async Task APeerUpdateThatAddsAPayloadWaitsUntilSetPlanGivesRoom()
    {
        var sam = await LoginAsOwner(Identities.Sam);
        var frodo = await LoginAsOwner(Identities.Frodo);
        var drive = TargetDrive.NewTargetDrive();
        await CollabChannelFlow.SetupAsync(frodo, drive, [sam]);

        var create = await sam.V1.PeerDirect.TransferNewFile(drive, SharedMetadata("before"), [frodo.Identity], null,
            new UploadManifest(), []);
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await PeerFlow.DistributeAsync(sam, frodo, drive);
        var remoteFile = create.Content!.RemoteGlobalTransitIdFileIdentifier.ToFileIdentifier();
        var stagedBefore = StagedUploadFiles(frodo, drive);

        await SetPlanAsync(Identities.Frodo, FullPlan);

        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        var update = await sam.V1.Drive.UpdateFile(new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Peer,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = remoteFile,
            Recipients = [frodo.Identity],
            Manifest = new UploadManifest
            {
                PayloadDescriptors =
                [
                    new UploadManifestPayloadDescriptor
                    {
                        PayloadUpdateOperationType = PayloadUpdateOperationType.AppendOrOverwrite,
                        Iv = Guid.Empty.ToByteArray(),
                        PayloadKey = payload.Key,
                        ContentType = payload.ContentType,
                        Thumbnails = []
                    }
                ]
            }
        }, SharedMetadata("after"), [payload]);
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        await PeerFlow.DistributeAsync(sam, frodo, drive);
        Assert.That(await PayloadKeysAsync(frodo, remoteFile), Is.Empty, "the payload is not on the full recipient");
        Assert.That(StagedUploadFiles(frodo, drive), Is.EquivalentTo(stagedBefore), "and nothing half-received left on disk");

        await SetPlanAsync(Identities.Frodo, DevPlan);
        await DeliverDeferredAsync(sam, frodo, drive);

        Assert.That(await PayloadKeysAsync(frodo, remoteFile), Is.EqualTo(new[] { payload.Key }), "the update lands once there is room");
    }

    //
    // Helpers
    //

    private async Task SetPlanAsync(string identity, string planId)
    {
        var response = await TestAdminApi.SetPlanAsync(Host, identity, planId);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"set-plan {identity} {planId}");
    }

    private static UploadManifest ManifestFor(List<TestPayloadDefinition> payloads) =>
        new() { PayloadDescriptors = payloads.ToPayloadDescriptorList().ToList() };

    private static UploadFileMetadata SharedMetadata(string content) => new()
    {
        AllowDistribution = true,
        IsEncrypted = false,
        AppData = new UploadAppFileMetaData { FileType = 100, Content = content },
        AccessControlList = AccessControlList.Connected
    };

    // A refusal defers the item by the recipient's Retry-After (an hour); bring it forward rather than wait
    private static async Task DeliverDeferredAsync(OwnerSession sender, OwnerSession recipient, TargetDrive drive)
    {
        await using (var scope = sender.Host.GetTenantScope(sender.Identity.DomainName).BeginLifetimeScope())
        {
            await scope.Resolve<PeerOutbox>().BringForwardScheduledItemsAsync();
        }

        await PeerFlow.DistributeAsync(sender, recipient, drive);
    }

    private static async Task<List<string>> PayloadKeysAsync(OwnerSession owner, FileIdentifier remoteFile)
    {
        var results = (await owner.V1.Drive.QueryByGlobalTransitId(remoteFile.ToGlobalTransitIdFileIdentifier())).Content!.SearchResults;
        return results.Single().FileMetadata.Payloads?.Select(p => p.Key).ToList() ?? [];
    }

    // A direct-write peer transfer or update stages its instructions and metadata here before any payload arrives
    private static List<string> StagedUploadFiles(OwnerSession owner, TargetDrive drive)
    {
        var paths = owner.Host.GetTenantScope(owner.Identity.DomainName).Resolve<TenantContext>().TenantPathManager;
        var folder = paths.GetDriveUploadPath(drive.Alias);
        return Directory.Exists(folder) ? Directory.GetFiles(folder, "*", SearchOption.AllDirectories).ToList() : [];
    }
}
