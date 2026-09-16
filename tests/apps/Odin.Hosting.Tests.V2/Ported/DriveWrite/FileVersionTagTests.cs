using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Hosting.Tests;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;
using Refit;

namespace Odin.Hosting.Tests.V2.Ported.DriveWrite;

/// <summary>
/// Port of <c>AppAPI/Drive/FileVersionTagTests</c>. The server assigns a version tag to a newly
/// uploaded file, and an overwrite carrying a stale version tag is refused with
/// <see cref="OdinClientErrorCode.VersionTagMismatch"/>.
/// </summary>
/// <remarks>
/// The original registered its app by hand (drive with anonymous reads off,
/// <see cref="DrivePermission.All"/> on it, <c>new PermissionSet(PermissionKeys.All)</c>), which is
/// <see cref="CallerSpec.App(DriveSpec, DrivePermission, IReadOnlyList{int})"/>. One live caller, so
/// no matrix and plain <c>[Test]</c> methods.
/// <para>
/// <c>AppDriveApiClient.UploadFile(drive, metadata, payload: "")</c> means "no payload", so the
/// first upload becomes <c>UploadNewMetadata</c>. The stale-tag overwrite keeps its own multipart
/// body (<see cref="UploadOverwriteAsync"/>) because it is the one request in the fixture that
/// combines <c>OverwriteFileId</c> with the default (not metadata-only) storage intent — the shape
/// <c>UploadRaw</c> sent and the one the version-tag check rejects. It goes through
/// <see cref="IUniversalDriveHttpClientApi"/>, the same V1 endpoint the original's
/// <c>IDriveTestHttpClientForApps</c> addressed.
/// </para>
/// </remarks>
[TestFixture]
public class FileVersionTagTests : V2Fixture
{
    /// <summary>The app the original registered: all drive permissions, all permission keys, non-anonymous drive.</summary>
    private static CallerSpec SampleApp() =>
        CallerSpec.App(new DriveSpec(TargetDrive.NewTargetDrive(), "Chat Drive 1", AllowAnonymousReads: false),
            DrivePermission.All, PermissionKeys.All);

    [Test]
    public async Task NewVersionTagSetWhenFileUploaded()
    {
        var spec = SampleApp();
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
        Assert.That(uploadResponse.IsSuccessStatusCode, Is.True);
        var uploadResult = uploadResponse.Content!;

        //get the uploaded file
        var uploadedFile = (await appApiClient.V1.Drive.GetFileHeader(uploadResult.File)).Content;

        Assert.That(uploadedFile!.FileMetadata.VersionTag, Is.Not.EqualTo(Guid.Empty),
            "Server should have set a VersionTag on a new file");
    }

    [Test]
    public async Task UploadStaleVersionTagFails_AndReturns_BadRequest_VersionTagMismatch()
    {
        var spec = SampleApp();
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
        Assert.That(uploadResponse.IsSuccessStatusCode, Is.True);
        var uploadResult = uploadResponse.Content!;

        //get the uploaded file
        var uploadedFile = (await appApiClient.V1.Drive.GetFileHeader(uploadResult.File)).Content;

        Assert.That(uploadedFile!.FileMetadata.VersionTag, Is.Not.EqualTo(Guid.Empty),
            "Server should have set a VersionTag on a new file");

        //just send a random token
        fileMetadata.VersionTag = Guid.Parse("7215bd54-c832-4f08-84fc-ebfb6193ee52");

        var apiResponse = await UploadOverwriteAsync(appApiClient, spec.TargetDrive, fileMetadata,
            overwriteFileId: uploadResult.File.FileId);

        Assert.That(apiResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var code = TestUtils.ParseProblemDetails(apiResponse!.Error!);
        Assert.That(code, Is.EqualTo(OdinClientErrorCode.VersionTagMismatch));
    }

    /// <summary>
    /// The original's <c>AppDriveApiClient.UploadRaw</c> with no payload: a full (not metadata-only)
    /// upload that names an existing file id to overwrite.
    /// </summary>
    private static async Task<ApiResponse<UploadResult>> UploadOverwriteAsync(
        IV2Caller caller, TargetDrive targetDrive, UploadFileMetadata fileMetadata, Guid overwriteFileId)
    {
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        var instructionSet = new UploadInstructionSet()
        {
            TransferIv = transferIv,
            StorageOptions = new()
            {
                Drive = targetDrive,
                OverwriteFileId = overwriteFileId
            },
            TransitOptions = new TransitOptions()
            {
            },
            Manifest = new UploadManifest()
        };

        var client = caller.Factory.CreateHttpClient(caller.Identity, out var sharedSecret);

        var instructionStream = new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());
        fileMetadata.IsEncrypted = false;

        var descriptor = new UploadFileDescriptor()
        {
            EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, instructionSet.TransferIv, ref sharedSecret),
            FileMetadata = fileMetadata
        };

        var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, instructionSet.TransferIv, ref sharedSecret);

        var parts = new List<StreamPart>
        {
            new(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
            new(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
        };

        var driveSvc = RestService.For<IUniversalDriveHttpClientApi>(client);
        var response = await driveSvc.UploadStream(parts.ToArray());
        keyHeader.AesKey.Wipe();
        return response;
    }
}
