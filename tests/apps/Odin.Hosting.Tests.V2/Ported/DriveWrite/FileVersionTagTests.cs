using System;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.DriveWrite;

/// <summary>
/// Port of <c>AppAPI/Drive/FileVersionTagTests</c>. The server assigns a version tag to a newly
/// uploaded file, and an overwrite carrying a stale version tag is refused with
/// <see cref="OdinClientErrorCode.VersionTagMismatch"/>.
/// </summary>
/// <remarks>
/// The original registered its app by hand (drive with anonymous reads off,
/// <see cref="DrivePermission.All"/> on it, <c>new PermissionSet(PermissionKeys.All)</c>), which is
/// <see cref="CallerSpec.SampleAppWithAllKeys"/>. One live caller, so no matrix and plain
/// <c>[Test]</c> methods.
/// <para>
/// <c>AppDriveApiClient.UploadFile(drive, metadata, payload: "")</c> means "no payload", so the
/// first upload becomes <c>UploadNewMetadata</c>. The stale-tag overwrite is the one request in the
/// fixture that combines <c>OverwriteFileId</c> with the default (not metadata-only) storage intent —
/// the shape <c>UploadRaw</c> sent and the one the version-tag check rejects — and it goes through
/// <see cref="AppFileUploads.TryUploadEncryptedAsync"/> with no payload, which posts to
/// <see cref="IUniversalDriveHttpClientApi"/>, the same V1 endpoint the original's
/// <c>IDriveTestHttpClientForApps</c> addressed. One difference from the original: that helper marks
/// the metadata <c>IsEncrypted = true</c> where <c>UploadRaw</c> forced it false. With no payload on
/// the request the version-tag check is reached either way, and the refusal asserted here is
/// unchanged.
/// </para>
/// </remarks>
[TestFixture]
public class FileVersionTagTests : V2Fixture
{
    [Test]
    public async Task NewVersionTagSetWhenFileUploaded()
    {
        var spec = CallerSpec.SampleAppWithAllKeys("Chat Drive 1");
        var appApiClient = await SetupCaller(spec);

        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = false,
            IsEncrypted = false,
            AppData = new()
            {
                Content = "some content",
                FileType = 101,
                GroupId = default,
                // UniqueId = message.Id,
            },
            VersionTag = default, //new file
            AccessControlList = AccessControlList.OwnerOnly
        };

        //upload a new file
        var uploadResponse = await appApiClient.V1.Drive.UploadNewMetadata(spec.TargetDrive, fileMetadata);
        Assert.That(uploadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var uploadResult = uploadResponse.Content!;

        //get the uploaded file
        var uploadedFile = (await appApiClient.V1.Drive.GetFileHeader(uploadResult.File)).Content;

        Assert.That(uploadedFile!.FileMetadata.VersionTag, Is.Not.EqualTo(Guid.Empty),
            "Server should have set a VersionTag on a new file");
    }

    [Test]
    public async Task UploadStaleVersionTagFails_AndReturns_BadRequest_VersionTagMismatch()
    {
        var spec = CallerSpec.SampleAppWithAllKeys("Chat Drive 1");
        var appApiClient = await SetupCaller(spec);

        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = false,
            IsEncrypted = false,
            AppData = new()
            {
                Content = "some content",
                FileType = 101,
                GroupId = default,
                // UniqueId = message.Id,
            },
            VersionTag = default, //new file
            AccessControlList = AccessControlList.OwnerOnly
        };

        //upload a new file
        var uploadResponse = await appApiClient.V1.Drive.UploadNewMetadata(spec.TargetDrive, fileMetadata);
        Assert.That(uploadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var uploadResult = uploadResponse.Content!;

        //get the uploaded file
        var uploadedFile = (await appApiClient.V1.Drive.GetFileHeader(uploadResult.File)).Content;

        Assert.That(uploadedFile!.FileMetadata.VersionTag, Is.Not.EqualTo(Guid.Empty),
            "Server should have set a VersionTag on a new file");

        //just send a random token
        fileMetadata.VersionTag = Guid.Parse("7215bd54-c832-4f08-84fc-ebfb6193ee52");

        var ctx = await AppFileUploads.TryUploadEncryptedAsync(appApiClient, spec.TargetDrive, fileMetadata,
            payloadData: null, overwriteFileId: uploadResult.File.FileId);

        Assert.That(ctx.Response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var code = TestUtils.ParseProblemDetails(ctx.Response.Error!);
        Assert.That(code, Is.EqualTo(OdinClientErrorCode.VersionTagMismatch));

        ctx.KeyHeader.AesKey.Wipe();
    }
}
