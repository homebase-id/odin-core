#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.DriveWrite;

/// <summary>
/// A file's <see cref="DataSource"/> across a change of the file (#2108).  A batch update compared the stored and the
/// sent data source by reference, so it refused every file that had one; it now compares them by value, and still refuses
/// bytes added to a file whose payloads are remote.  An overwrite whose new payloads are remote deleted none of the old
/// file's payloads stored here.
/// </summary>
/// <remarks>Sam is only named as the data source; nothing contacts him, so only Frodo is hosted.</remarks>
[TestFixture]
public class DataSourceUpdateTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo];

    private const int FileType = 101;

    [Test]
    public async Task ABatchUpdateNamingTheSameDataSourceIsAccepted()
    {
        var (frodo, drive, file) = await RemoteFileAsync();

        var metadata = RemoteMetadata(drive, "changed");
        metadata.VersionTag = file.NewVersionTag;
        var update = await frodo.V1.Drive.UpdateFile(Instructions(file, []), metadata, payloads: []);

        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK), update.Error?.Content);
        var header = await frodo.V1.Drive.GetFileHeader(file.File);
        Assert.That(header.Content!.FileMetadata.AppData.Content, Is.EqualTo("changed"));
        Assert.That(header.Content.FileMetadata.DataSource.PayloadsAreRemote, Is.True);
    }

    [Test]
    public async Task ABatchUpdateNamingAnotherDataSourceIsRefused()
    {
        var (frodo, drive, file) = await RemoteFileAsync();

        var metadata = RemoteMetadata(drive, "changed");
        metadata.DataSource = new DataSource { Identity = new OdinId(Identities.Pippin), DriveId = drive.Alias, PayloadsAreRemote = true };
        metadata.VersionTag = file.NewVersionTag;
        var update = await frodo.V1.Drive.UpdateFile(Instructions(file, []), metadata, payloads: []);

        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), update.Error?.Content);
        Assert.That(TestUtils.ParseProblemDetails(update.Error!), Is.EqualTo(OdinClientErrorCode.CannotModifyRemotePayloadIdentity));
        await AssertUnchangedAsync(frodo, file);
    }

    [Test]
    public async Task ABatchUpdateCannotAddAPayloadToAFileWhosePayloadsAreRemote()
    {
        var (frodo, drive, file) = await RemoteFileAsync();

        List<TestPayloadDefinition> added = [SamplePayloadDefinitions.GetPayloadDefinition1()];
        var metadata = RemoteMetadata(drive, "changed");
        metadata.VersionTag = file.NewVersionTag;
        var update = await frodo.V1.Drive.UpdateFile(Instructions(file, added), metadata, added);

        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "bytes stored here for a file whose payloads are remote");
        Assert.That(TestUtils.ParseProblemDetails(update.Error!), Is.EqualTo(OdinClientErrorCode.CannotModifyRemotePayloadIdentity));
        AssertNoPayloadFiles(frodo, drive);
        await AssertUnchangedAsync(frodo, file);
    }

    [Test]
    public async Task OverwritingAFileWithLocalPayloadsByOneWhosePayloadsAreRemoteDeletesTheOldOnes()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var drive = await NewDriveAsync(frodo);
        List<TestPayloadDefinition> payloads = [SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1()];
        var local = await frodo.V1.Drive.UploadNewFile(drive, SampleMetadataData.CreateWithContent(FileType, "stored here"),
            Manifest(payloads), payloads);
        Assert.That(local.StatusCode, Is.EqualTo(HttpStatusCode.OK), local.Error?.Content);
        var stored = await StoredObjectPathsAsync(frodo, drive, local.Content!.File.FileId);
        var store = Host.GetTenantScope(frodo.Identity.DomainName).Resolve<LongTermPayloadStore>();
        Assert.That(stored, Has.Count.EqualTo(2), "precondition: a payload and its thumbnail");
        foreach (var path in stored)
        {
            Assert.That(await store.ExistsAsync(path), Is.True, $"precondition: stored before the overwrite: {path}");
        }

        var remote = RemoteMetadata(drive, "payloads elsewhere");
        remote.VersionTag = local.Content.NewVersionTag;
        var overwrite = await frodo.V1.Drive.UploadNewFile(drive, remote, Manifest(payloads), payloads: [],
            overwriteFileId: local.Content.File.FileId);
        Assert.That(overwrite.StatusCode, Is.EqualTo(HttpStatusCode.OK), overwrite.Error?.Content);

        var header = (await frodo.V1.Drive.GetFileHeader(local.Content.File)).Content!;
        Assert.That(header.FileMetadata.DataSource?.PayloadsAreRemote, Is.True);
        Assert.That(header.FileMetadata.Payloads.Select(p => p.Key), Is.EquivalentTo(payloads.Select(p => p.Key)));
        Assert.That(header.FileMetadata.Payloads.Single().BytesWritten, Is.EqualTo(0), "the header lists the remote payload, not the old one");
        foreach (var path in stored)
        {
            Assert.That(await store.ExistsAsync(path), Is.False, $"left in the store, listed by no file: {path}");
        }
    }

    //

    private static async Task AssertUnchangedAsync(OwnerSession frodo, UploadResult file)
    {
        var header = (await frodo.V1.Drive.GetFileHeader(file.File)).Content!;
        Assert.That(header.FileMetadata.AppData.Content, Is.EqualTo("first"));
        Assert.That(header.FileMetadata.VersionTag, Is.EqualTo(file.NewVersionTag));
    }

    private static UploadFileMetadata RemoteMetadata(TargetDrive drive, string content)
    {
        var metadata = SampleMetadataData.CreateWithContent(FileType, content);
        metadata.DataSource = new DataSource { Identity = new OdinId(Identities.Sam), DriveId = drive.Alias, PayloadsAreRemote = true };
        return metadata;
    }

    private static UploadManifest Manifest(List<TestPayloadDefinition> payloads) =>
        new() { PayloadDescriptors = payloads.ToPayloadDescriptorList().ToList() };

    private static FileUpdateInstructionSet Instructions(UploadResult file, List<TestPayloadDefinition> added) => new()
    {
        Locale = UpdateLocale.Local,
        TransferIv = ByteArrayUtil.GetRndByteArray(16),
        File = file.File.ToFileIdentifier(),
        Manifest = new UploadManifest
        {
            PayloadDescriptors = added.Select(p => p.ToPayloadDescriptor(PayloadUpdateOperationType.AppendOrOverwrite)).ToList()
        }
    };

    private static async Task<TargetDrive> NewDriveAsync(OwnerSession owner)
    {
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "data source", allowAnonymousReads: false);
        return drive;
    }

    /// <summary>A file on a new drive of Frodo's whose one payload is Sam's</summary>
    private async Task<(OwnerSession Frodo, TargetDrive Drive, UploadResult File)> RemoteFileAsync()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var drive = await NewDriveAsync(frodo);
        var upload = await frodo.V1.Drive.UploadNewFile(drive, RemoteMetadata(drive, "first"),
            Manifest([SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1()]), payloads: []);
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);
        return (frodo, drive, upload.Content!);
    }
}
