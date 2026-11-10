#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Tasks;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Ported.DataSubscription;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.DriveWrite;

/// <summary>
/// Deleting a file deletes the payloads this identity stores for it, on any drive, and looks for none when its payloads
/// are remote: those live with the identity they came from (a followed post on the feed drive), so there is nothing here
/// to delete.  A remote payload looked for would log an Error per payload and thumbnail, which the fixture fails on.  So
/// that stays true, no payload can be added to a file whose payloads are remote.
/// </summary>
[TestFixture]
public class PayloadDeletePerFileTests : V2Fixture
{
    public enum Delete { Soft, Hard }

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task DeletingAFollowedPostLooksForNoneOfItsRemotePayloads([Values] Delete delete)
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var channel = await DataSubscriptionScenario.CreateChannelDriveAsync(sam, allowAnonymousReads: true);
        await DataSubscriptionScenario.FollowAsync(frodo, sam);

        var metadata = SampleMetadataData.CreateWithContent(FileType, "Sam's post", AccessControlList.Anonymous);
        metadata.AllowDistribution = true;
        var post = await UploadAsync(sam, channel, metadata, local: true);
        await sam.Sync.DrainOutboxAsync();
        await frodo.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        var item = await DataSubscriptionScenario.AssertFeedDriveHasFileAsync(frodo,
            DataSubscriptionScenario.FeedQueryByGlobalTransitId(post), "Sam's post", post);
        Assert.That((item.FileMetadata.DataSource?.PayloadsAreRemote, item.FileMetadata.Payloads.Count()), Is.EqualTo(((bool?)true, 2)),
            "precondition: Frodo's copy lists Sam's two payloads, as remote");

        await DeleteAsync(frodo, WellKnownAppDrives.FeedDrive, item.FileId, item.FileMetadata.VersionTag, delete);
        // The fixture fails on any Error the delete logged
    }

    [Test]
    public async Task DeletingAFileWithRemotePayloadsOnAnyDriveLooksForNoneOfThem([Values] Delete delete)
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var drive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(drive, "remote payloads", allowAnonymousReads: false);

        var metadata = SampleMetadataData.CreateWithContent(FileType, "payloads elsewhere");
        metadata.DataSource = new DataSource { Identity = new OdinId(Identities.Sam), DriveId = drive.Alias, PayloadsAreRemote = true };
        var file = await UploadAsync(frodo, drive, metadata, local: false);

        await DeleteAsync(frodo, drive, file.File.FileId, file.NewVersionTag, delete);
        // The fixture fails on any Error the delete logged
    }

    [Test]
    public async Task APayloadCannotBeAddedToAFileWhosePayloadsAreRemote()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var drive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(drive, "remote payloads", allowAnonymousReads: false);
        var metadata = SampleMetadataData.CreateWithContent(FileType, "payloads elsewhere");
        metadata.DataSource = new DataSource { Identity = new OdinId(Identities.Sam), DriveId = drive.Alias, PayloadsAreRemote = true };
        var file = await UploadAsync(frodo, drive, metadata, local: false);

        List<TestPayloadDefinition> added = [SamplePayloadDefinitions.GetPayloadDefinition1()];
        var upload = await frodo.V1.Drive.UploadPayloads(file.File, file.NewVersionTag,
            new UploadManifest { PayloadDescriptors = added.ToPayloadDescriptorList().ToList() }, added);

        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "bytes stored here for a file whose payloads are remote");
        Assert.That(TestUtils.ParseProblemDetails(upload.Error!), Is.EqualTo(OdinClientErrorCode.CannotModifyRemotePayloadIdentity));
        AssertNoPayloadFiles(frodo, drive);
    }

    [Test]
    public async Task DeletingAFileWithLocalPayloadsOnTheFeedDriveDeletesThem([Values] Delete delete)
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var file = await UploadAsync(frodo, WellKnownAppDrives.FeedDrive,
            SampleMetadataData.CreateWithContent(FileType, "stored here"), local: true);
        var stored = await StoredObjectPathsAsync(frodo, WellKnownAppDrives.FeedDrive, file.File.FileId);
        Assert.That(stored, Has.Count.EqualTo(4), "precondition: two payloads with a thumbnail each");
        var store = Host.GetTenantScope(frodo.Identity.DomainName).Resolve<LongTermPayloadStore>();
        foreach (var path in stored)
        {
            Assert.That(await store.ExistsAsync(path), Is.True, $"precondition: stored before the delete: {path}");
        }

        await DeleteAsync(frodo, WellKnownAppDrives.FeedDrive, file.File.FileId, file.NewVersionTag, delete);

        foreach (var path in stored)
        {
            Assert.That(await store.ExistsAsync(path), Is.False, $"left in the store after the delete: {path}");
        }
    }

    private const int FileType = 101;

    // A file with two payloads, each with a thumbnail; their bytes go along only when they're stored here
    private static async Task<UploadResult> UploadAsync(OwnerSession owner, TargetDrive drive, UploadFileMetadata metadata, bool local)
    {
        List<TestPayloadDefinition> payloads =
        [
            SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1(),
            SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail2()
        ];
        var upload = await owner.V1.Drive.UploadNewFile(drive, metadata,
            new UploadManifest { PayloadDescriptors = payloads.ToPayloadDescriptorList().ToList() }, local ? payloads : []);
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);
        return upload.Content!;
    }

    // Deletes the file, then waits for the payload deletes a hard delete leaves running in the background
    private async Task DeleteAsync(OwnerSession owner, TargetDrive drive, Guid fileId, Guid versionTag, Delete delete)
    {
        if (delete == Delete.Soft)
        {
            var soft = await owner.Drives.Writer.SoftDeleteFile(drive.Alias, fileId);
            Assert.That(soft.StatusCode, Is.EqualTo(HttpStatusCode.OK), soft.Error?.Content);
        }
        else
        {
            var hard = await owner.Drives.Writer.HardDeleteBatch(drive.Alias, new HardDeleteBatchRequestV2
            {
                Files = [new FileVersionTagV2 { FileId = fileId, VersionTag = versionTag }]
            });
            Assert.That((hard.StatusCode, hard.Content?.Results.Single().ErrorCode), Is.EqualTo((HttpStatusCode.OK, (OdinClientErrorCode?)null)),
                hard.Error?.Content);
        }

        await Host.Server.Services.GetRequiredService<IForgottenTasks>().WhenAll();
    }
}
