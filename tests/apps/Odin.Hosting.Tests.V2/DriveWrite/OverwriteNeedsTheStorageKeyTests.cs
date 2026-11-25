#nullable enable
using System;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.DriveWrite;

/// <summary>
/// On a normal drive, every write that replaces a file needs the drive's storage key, even when the file is
/// unencrypted: a caller may lack it while holding Write (a guest on a public drive: its grants carry none), and Write
/// alone lets them add files, not change other people's (#2026).  A full overwrite (NewFileOrOverwrite) asks for it as
/// it locks a key header; a metadata-only overwrite and the update endpoint ask for it outright.  Only a zero-knowledge
/// drive, which has no storage key to ask for, skips it.  The owner's file is readable by the guest (anonymous ACL),
/// so the read rule alone would let each of these through.
/// </summary>
[TestFixture]
public class OverwriteNeedsTheStorageKeyTests : V2Fixture
{
    private static readonly CallerSpec GuestWriter = CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Write);

    [Test]
    public async Task ACallerWithoutTheStorageKeyCannotOverwriteAFileEvenWithAnUnencryptedOne()
    {
        var (guest, owner) = await SetupCallerWithOwner(GuestWriter);
        var (file, versionTag) = await OwnerUploadsAnAnonymousFile(owner);

        var replacement = SampleMetadataData.Create(fileType: 101, acl: AccessControlList.Anonymous);
        replacement.VersionTag = versionTag;
        var overwrite = await guest.V1.Drive.UploadNewMetadata(GuestWriter.TargetDrive, replacement, new TransitOptions(),
            overwriteFileId: file.FileId);
        Assert.That(overwrite.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), overwrite.Error?.Content);

        await AssertUnchanged(owner, file);
    }

    [Test]
    public async Task ACallerWithoutTheStorageKeyCannotOverwriteAnUnencryptedFilesMetadata()
    {
        var (guest, owner) = await SetupCallerWithOwner(GuestWriter);
        var (file, versionTag) = await OwnerUploadsAnAnonymousFile(owner);

        var replacement = SampleMetadataData.Create(fileType: 101, acl: AccessControlList.Anonymous);
        replacement.AppData.Content = "guest wrote this";
        var update = await guest.V1.Drive.UpdateExistingMetadata(file, versionTag, replacement);
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), update.Error?.Content);

        await AssertUnchanged(owner, file);
    }

    [Test]
    public async Task ACallerWithoutTheStorageKeyCannotUpdateAnUnencryptedFile()
    {
        var (guest, owner) = await SetupCallerWithOwner(GuestWriter);
        var (file, versionTag) = await OwnerUploadsAnAnonymousFile(owner);

        var replacement = SampleMetadataData.Create(fileType: 101, acl: AccessControlList.Anonymous);
        replacement.AppData.Content = "guest wrote this";
        replacement.VersionTag = versionTag;
        var update = await guest.V1.Drive.UpdateFile(new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Local,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = file.ToFileIdentifier(),
            Manifest = new UploadManifest { PayloadDescriptors = [] }
        }, replacement, []);
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), update.Error?.Content);

        await AssertUnchanged(owner, file);
    }

    [Test]
    public async Task ACallerWithoutTheStorageKeyStillAddsFiles()
    {
        var (guest, owner) = await SetupCallerWithOwner(GuestWriter);

        var metadata = SampleMetadataData.Create(fileType: 102, acl: AccessControlList.Anonymous);
        var upload = await guest.V1.Drive.UploadNewMetadata(GuestWriter.TargetDrive, metadata, new TransitOptions());
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);

        var read = await owner.V1.Drive.GetFileHeader(upload.Content!.File);
        Assert.That(read.Content!.FileMetadata.AppData.FileType, Is.EqualTo(102));
    }

    private async Task<(ExternalFileIdentifier File, Guid VersionTag)> OwnerUploadsAnAnonymousFile(OwnerSession owner)
    {
        var original = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Anonymous);
        var upload = await owner.V1.Drive.UploadNewMetadata(GuestWriter.TargetDrive, original, new TransitOptions());
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);
        return (upload.Content!.File, upload.Content.NewVersionTag);
    }

    private static async Task AssertUnchanged(OwnerSession owner, ExternalFileIdentifier file)
    {
        var read = await owner.V1.Drive.GetFileHeader(file);
        Assert.That(read.Content!.FileMetadata.AppData.FileType, Is.EqualTo(100), "the owner's file is unchanged");
    }
}
