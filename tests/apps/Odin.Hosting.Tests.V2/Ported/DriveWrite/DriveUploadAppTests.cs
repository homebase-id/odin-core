using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Hosting.Tests;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;

namespace Odin.Hosting.Tests.V2.Ported.DriveWrite;

/// <summary>
/// Port of <c>AppAPI/Drive/DriveUploadAppTests</c>. An app uploading an encrypted file with a
/// separately-IV'd payload and reading both back; an encrypted file with an Anonymous
/// required-security-group being refused; and a client unique id becoming free again once the file
/// holding it is soft-deleted.
/// </summary>
/// <remarks>
/// The original's arrange was <c>_scaffold.OldOwnerApi.SetupTestSampleApp(identity)</c>: owner
/// creates a drive (anonymous reads off) and registers an app with <see cref="DrivePermission.All"/>
/// on it plus <see cref="PermissionKeys.UseTransitWrite"/> — i.e. <see cref="CallerSpec.SampleApp"/>.
/// One live caller, so no matrix and plain <c>[Test]</c> methods. The multipart uploads go through
/// <see cref="AppFileUploads"/>, which posts to <see cref="IUniversalDriveHttpClientApi"/> — the same
/// V1 endpoints the original's <c>IDriveTestHttpClientForApps</c> addressed, resolved against
/// <c>/api/apps/v1</c> by the app factory's path handler.
/// <para>
/// <see cref="CanUploadFile"/> passes its own payload IV, which the shared helper then both declares
/// in the manifest and encrypts the payload under — the round trip this test is about.
/// <see cref="CannotUploadEncryptedFileForAnonymousGroups"/> sends one difference from the original:
/// the original's instruction set named no payload in its manifest while still posting a payload part,
/// where the helper declares it. The refusal under test is the Anonymous-plus-encrypted check either
/// way; the original asserted only "not a success", this pins the <c>400</c> it actually returns.
/// </para>
/// <para>
/// <b>Carried oddity — <see cref="CanReuseUniqueIdAfterSoftDelete"/> never uses the app.</b> It
/// registers the sample app only to get a drive, then does every call as the <b>owner</b>
/// (<c>new OwnerApiClient(...)</c> in the original). Reproduced as written: the app caller is built
/// so the arrange matches, and the test then acts through <c>owner.V1.Drive</c>. Note also that the
/// original's <c>DriveApiClient.UploadFile</c> forces <c>IsEncrypted = false</c> on the way out, so
/// the <c>IsEncrypted = true</c> in both file metadata objects has never reached the server; the
/// metadata-only <c>UploadNewMetadata</c> used here does the same.
/// </para>
/// </remarks>
[TestFixture]
public class DriveUploadAppTests : V2Fixture
{
    [Test(Description = "Test Upload only; no expire, no drive; no transfer")]
    public async Task CanUploadFile()
    {
        var spec = CallerSpec.SampleApp();
        var caller = await SetupCaller(spec);

        var payloadIv = ByteArrayUtil.GetRndByteArray(16);
        const string payloadDataRaw = "{payload:true, image:'b64 data'}";

        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = false,
            IsEncrypted = true,
            AppData = new()
            {
                Tags = new List<Guid>() { Guid.NewGuid(), Guid.NewGuid() },
                Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" })
            }
        };

        var ctx = await AppFileUploads.UploadEncryptedAsync(caller, spec.TargetDrive, fileMetadata, payloadDataRaw,
            payloadIv: payloadIv);

        Assert.That(ctx.UploadResult.RecipientStatus, Is.Null);

        var targetDrive = ctx.UploadResult.File.TargetDrive;
        var fileId = ctx.UploadResult.File.FileId;

        //retrieve the file that was uploaded; decrypt;
        var client = caller.Factory.CreateHttpClient(caller.Identity, out var sharedSecret);
        var driveSvc = RefitCreator.RestServiceFor<IUniversalDriveHttpClientApi>(client, sharedSecret);

        var fileResponse = await driveSvc.GetFileHeaderAsPost(new ExternalFileIdentifier() { TargetDrive = targetDrive, FileId = fileId });

        Assert.That(fileResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(fileResponse.Content, Is.Not.Null);

        var clientFileHeader = fileResponse.Content;

        Assert.That(clientFileHeader!.FileMetadata, Is.Not.Null);
        Assert.That(clientFileHeader.FileMetadata.AppData, Is.Not.Null);

        Assert.That(clientFileHeader.FileMetadata.AppData.Tags, Is.EquivalentTo(fileMetadata.AppData.Tags));
        Assert.That(clientFileHeader.FileMetadata.AppData.Content, Is.EqualTo(fileMetadata.AppData.Content));
        Assert.That(clientFileHeader.FileMetadata.Payloads.Count, Is.EqualTo(1));

        Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader, Is.Not.Null);
        Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.Null);
        Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv.Length, Is.GreaterThanOrEqualTo(16));
        Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.EqualTo(Guid.Empty.ToByteArray()), "Iv was all zeros");
        Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.Aes));

        var decryptedKeyHeader = clientFileHeader.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref sharedSecret);

        Assert.That(decryptedKeyHeader.AesKey.IsSet(), Is.True);
        var fileKey = decryptedKeyHeader.AesKey;
        Assert.That(fileKey, Is.Not.EqualTo(Guid.Empty.ToByteArray()));

        //get the payload and decrypt, then compare
        var payloadResponse = await driveSvc.GetPayloadPost(new GetPayloadRequest()
        {
            File = new ExternalFileIdentifier() { TargetDrive = targetDrive, FileId = fileId },
            Key = WebScaffold.PAYLOAD_KEY
        });
        Assert.That(payloadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(payloadResponse.Content, Is.Not.Null);

        var payloadResponseCipher = await payloadResponse.Content!.ReadAsByteArrayAsync();
        Assert.That(ctx.PayloadCipher, Is.EqualTo(payloadResponseCipher));

        var aesKey = decryptedKeyHeader.AesKey;
        var decryptedPayloadBytes = AesCbc.Decrypt(
            cipherText: payloadResponseCipher,
            key: aesKey,
            iv: payloadIv);

        var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payloadDataRaw);
        Assert.That(payloadBytes, Is.EqualTo(decryptedPayloadBytes));

        decryptedKeyHeader.AesKey.Wipe();
        ctx.KeyHeader.AesKey.Wipe();
    }

    [Test]
    public async Task CannotUploadEncryptedFileForAnonymousGroups()
    {
        var spec = CallerSpec.SampleApp();
        var caller = await SetupCaller(spec);

        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = true,
            IsEncrypted = true,
            AppData = new()
            {
                Tags = new List<Guid>() { Guid.NewGuid(), Guid.NewGuid() },
                Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" })
            },
            AccessControlList = new AccessControlList()
            {
                RequiredSecurityGroup = SecurityGroupType.Anonymous
            }
        };

        var ctx = await AppFileUploads.TryUploadEncryptedAsync(caller, spec.TargetDrive, fileMetadata);

        Assert.That(ctx.Response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        ctx.KeyHeader.AesKey.Wipe();
    }

    [Test]
    public async Task CanReuseUniqueIdAfterSoftDelete()
    {
        var spec = CallerSpec.SampleApp();
        var (_, ownerClient) = await SetupCallerWithOwner(spec);
        var targetDrive = spec.TargetDrive;

        var firstUniqueId = Guid.NewGuid();
        var firstFileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = false,
            IsEncrypted = true,
            AppData = new()
            {
                Tags = new List<Guid>() { Guid.NewGuid(), Guid.NewGuid() },
                Content = OdinSystemSerializer.Serialize(new { message = "some data" }),
                UniqueId = firstUniqueId
            }
        };

        var firstFileResponse = await ownerClient.V1.Drive.UploadNewMetadata(targetDrive, firstFileMetadata, FileSystemType.Standard);
        Assert.That(firstFileResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var firstFile = firstFileResponse.Content!;

        //
        // Validate File was uploaded
        //
        var getFirstFileResponse = (await ownerClient.V1.Drive.GetFileHeader(firstFile.File, FileSystemType.Standard)).Content;
        Assert.That(getFirstFileResponse!.FileMetadata.AppData.Content, Is.EqualTo(firstFileMetadata.AppData.Content));
        Assert.That(getFirstFileResponse.FileMetadata.AppData.UniqueId, Is.EqualTo(firstFileMetadata.AppData.UniqueId));

        //
        // Can get first file by uniqueId
        //
        var getFirstFileByUniqueId = await QueryByUniqueId(ownerClient, firstFile.File.TargetDrive, firstUniqueId);
        Assert.That(getFirstFileByUniqueId, Is.Not.Null);
        Assert.That(getFirstFileByUniqueId!.FileMetadata.AppData.Content, Is.EqualTo(firstFileMetadata.AppData.Content));
        Assert.That(getFirstFileByUniqueId.FileMetadata.AppData.UniqueId, Is.EqualTo(firstFileMetadata.AppData.UniqueId));

        //
        // Delete the first file
        //

        await ownerClient.V1.Drive.SoftDeleteFile(firstFile.File);

        //
        // Validate first file is gone
        //

        var getFirstFileDeleted = await QueryByUniqueId(ownerClient, firstFile.File.TargetDrive, firstUniqueId);
        Assert.That(getFirstFileDeleted, Is.Null);

        //
        // Reuse the unique Id
        //

        var secondFileMeta = new UploadFileMetadata()
        {
            AllowDistribution = false,
            IsEncrypted = true,
            AppData = new()
            {
                Tags = new List<Guid>() { Guid.NewGuid(), Guid.NewGuid() },
                Content = OdinSystemSerializer.Serialize(new { message = "this is content in a second file that reuses a uniqueId" }),
                UniqueId = firstUniqueId
            }
        };

        var secondFileResponse = await ownerClient.V1.Drive.UploadNewMetadata(targetDrive, secondFileMeta, FileSystemType.Standard);
        Assert.That(secondFileResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var secondFile = secondFileResponse.Content!;

        //
        // Validate File was uploaded
        //
        var getSecondFileResponse = (await ownerClient.V1.Drive.GetFileHeader(secondFile.File, FileSystemType.Standard)).Content;
        Assert.That(getSecondFileResponse!.FileMetadata.AppData.Content, Is.EqualTo(secondFileMeta.AppData.Content));
        Assert.That(getSecondFileResponse.FileMetadata.AppData.UniqueId, Is.EqualTo(secondFileMeta.AppData.UniqueId));

        //
        // Can get first file by uniqueId
        //
        var getSecondFileByUniqueId = await QueryByUniqueId(ownerClient, firstFile.File.TargetDrive, firstUniqueId);
        Assert.That(getSecondFileByUniqueId, Is.Not.Null);
        Assert.That(getSecondFileByUniqueId!.FileMetadata.AppData.Content, Is.EqualTo(secondFileMeta.AppData.Content));
        Assert.That(getSecondFileByUniqueId.FileMetadata.AppData.UniqueId, Is.EqualTo(secondFileMeta.AppData.UniqueId));
    }

    /// <summary>
    /// The original's <c>DriveApiClient.QueryByUniqueId</c>: one record, metadata header included,
    /// <c>SingleOrDefault</c> so a miss reads as null.
    /// </summary>
    private static async Task<Odin.Services.Apps.SharedSecretEncryptedFileHeader> QueryByUniqueId(
        OwnerSession owner, TargetDrive targetDrive, Guid uniqueId)
    {
        var response = await owner.V1.Drive.QueryBatch(new QueryBatchRequest()
        {
            QueryParams = new FileQueryParamsV1()
            {
                TargetDrive = targetDrive,
                ClientUniqueIdAtLeastOne = new List<Guid>() { uniqueId }
            },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest()
            {
                MaxRecords = 1,
                IncludeMetadataHeader = true
            }
        }, FileSystemType.Standard);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content, Is.Not.Null);
        return response.Content!.SearchResults.SingleOrDefault();
    }
}
