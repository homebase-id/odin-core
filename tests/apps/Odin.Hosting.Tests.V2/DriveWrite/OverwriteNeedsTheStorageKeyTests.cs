#nullable enable
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.DriveWrite;

/// <summary>
/// On a normal drive, overwriting a file locks a key header even when the new file is unencrypted, so it needs
/// the drive's storage key.  That is what keeps a caller who may write but holds no storage key (a guest on a
/// public drive: its grants carry none) from replacing other people's files, comments included.  Only a
/// zero-knowledge drive, which has no storage key to ask for, skips it (#2023, review round 2).
/// </summary>
[TestFixture]
public class OverwriteNeedsTheStorageKeyTests : V2Fixture
{
    [Test]
    public async Task ACallerWithoutTheStorageKeyCannotOverwriteAFileEvenWithAnUnencryptedOne()
    {
        var spec = CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Write);
        var (guest, owner) = await SetupCallerWithOwner(spec);

        var original = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Anonymous);
        var upload = await owner.V1.Drive.UploadNewMetadata(spec.TargetDrive, original, new TransitOptions());
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);
        var file = upload.Content!.File;

        var replacement = SampleMetadataData.Create(fileType: 101, acl: AccessControlList.Anonymous);
        replacement.VersionTag = upload.Content.NewVersionTag;
        var overwrite = await guest.V1.Drive.UploadNewMetadata(spec.TargetDrive, replacement, new TransitOptions(),
            overwriteFileId: file.FileId);
        Assert.That(overwrite.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), overwrite.Error?.Content);

        var read = await owner.V1.Drive.GetFileHeader(file);
        Assert.That(read.Content!.FileMetadata.AppData.FileType, Is.EqualTo(100), "the owner's file is unchanged");
    }
}
