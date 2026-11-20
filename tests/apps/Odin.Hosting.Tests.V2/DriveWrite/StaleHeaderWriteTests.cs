#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.DriveWrite;

/// <summary>
/// A write that rebuilds a file's header from the one it read must not save it over a later version: the write is
/// conditional only on the caller's version tag, so a header read stale (a lost cache invalidation, #2093) would pass that
/// check and undo whatever changed since.  The server is made to read a stale header (<see cref="V2Fixture.ServeStaleHeaderRowAsync"/>).
/// </summary>
[TestFixture]
public class StaleHeaderWriteTests : V2Fixture
{
    [Test]
    public async Task DeletingAPayloadOverAStaleHeaderIsRefusedAndKeepsTheEditSinceIt()
    {
        var owner = await LoginAsOwner();
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "stale header", allowAnonymousReads: false);

        List<TestPayloadDefinition> payloads =
            [SamplePayloadDefinitions.GetPayloadDefinition1(), SamplePayloadDefinitions.GetPayloadDefinition2()];
        var metadata = new UploadFileMetadata
        {
            IsEncrypted = false,
            AppData = new UploadAppFileMetaData { Content = "before the edit", FileType = 101 },
            AccessControlList = AccessControlList.OwnerOnly
        };
        var upload = await owner.V1.Drive.UploadNewFile(drive, metadata,
            new UploadManifest { PayloadDescriptors = payloads.ToPayloadDescriptorList().ToList() }, payloads);
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);
        var file = upload.Content!.File;
        var beforeTheEdit = await HeaderRowAsync(owner, drive.Alias, file.FileId);

        // An edit since: the content changes, the payloads stay
        metadata.AppData.Content = "the edit";
        var edit = await owner.V1.Drive.UpdateExistingMetadata(file, upload.Content.NewVersionTag, metadata);
        Assert.That(edit.StatusCode, Is.EqualTo(HttpStatusCode.OK), edit.Error?.Content);

        // The caller knows the current version; the server reads the header as it was before the edit
        await ServeStaleHeaderRowAsync(owner, beforeTheEdit);
        var delete = await owner.V1.Drive.DeletePayload(file, edit.Content!.NewVersionTag, payloads[1].Key);

        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "a delete built on a header at another version");
        Assert.That(TestUtils.ParseProblemDetails(delete.Error!), Is.EqualTo(OdinClientErrorCode.VersionTagMismatch));

        await ForgetCachedHeaderRowsAsync(owner);
        var header = (await owner.V1.Drive.GetFileHeader(file)).Content!;
        Assert.That((header.FileMetadata.AppData.Content, header.FileMetadata.VersionTag),
            Is.EqualTo(("the edit", edit.Content.NewVersionTag)), "the edit stands");
        Assert.That(header.FileMetadata.Payloads.Select(p => p.Key), Is.EquivalentTo(payloads.Select(p => p.Key)),
            "both payloads are still listed");
    }
}
