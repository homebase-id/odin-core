using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Serialization;
using Odin.Hosting.Controllers.Base.Drive;
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
using QueryModifiedRequest = Odin.Services.Drives.QueryModifiedRequest;

namespace Odin.Hosting.Tests.V2.Ported.DriveWrite;

/// <summary>
/// Port of <c>AppAPI/Drive/DriveFileManagementTests</c>. An app uploading an encrypted file with a
/// payload and reading it back decrypted, and the shape of a soft delete seen from the app: the file
/// stays in the index (query-batch and query-modified both still return it, marked deleted), the
/// header still resolves, and the payload and thumbnail are gone.
/// </summary>
/// <remarks>
/// The original's arrange was <c>_scaffold.OldOwnerApi.SetupTestSampleApp(identity)</c> /
/// <c>_scaffold.AppApi.CreateAppAndUploadFileMetadata(...)</c>: owner creates a drive
/// (anonymous reads off) and registers an app holding <see cref="DrivePermission.All"/> on it plus
/// <see cref="PermissionKeys.UseTransitWrite"/> — and, for the upload-helper path,
/// <see cref="PermissionKeys.ReadConnections"/> / <see cref="PermissionKeys.ReadConnectionRequests"/>.
/// That is exactly <see cref="CallerSpec.SampleApp"/> / <see cref="CallerSpec.SampleAppWithConnectionReads"/>,
/// so the port uses <c>SetupCaller</c>. One live caller, so no matrix and a plain <c>[Test]</c>. The
/// original's circle-with-drive creation is dropped: with no transit recipients nothing reads it.
/// <para>
/// Both uploads go through <see cref="AppFileUploads.UploadEncryptedAsync"/>, which sends the same
/// multipart body these tests used to build by hand — including the payload-IV handling
/// <see cref="UploadOnly"/> round-trips: the payload is encrypted under the file key header's own IV
/// while the manifest declares an unrelated random one. It posts to
/// <see cref="IUniversalDriveHttpClientApi"/>, which addresses the same V1 endpoints the original's
/// <c>IDriveTestHttpClientForApps</c> did — resolved against <c>/api/apps/v1</c> by the app
/// factory's path handler.
/// </para>
/// <para>
/// <c>CanHardDeleteFile</c> keeps its <c>[Ignore("There is no api exposed for hard-delete for an
/// App.")]</c> and its empty body verbatim.
/// </para>
/// <para>
/// <b>Carried defect.</b> In <see cref="CanSoftDeleteFile"/> the "crucial" query-modified assertion
/// reads <c>qbResponse</c> — the earlier query-batch response — not
/// <c>queryModifiedResponse</c>. Beyond a success-code check, nothing in the test actually asserts
/// on the query-modified result. Carried verbatim.
/// </para>
/// </remarks>
[TestFixture]
public class DriveFileManagementTests : V2Fixture
{
    [Test(Description = "Test Upload only; no expire, no drive; no transfer")]
    public async Task UploadOnly()
    {
        var spec = CallerSpec.SampleApp();
        var caller = await SetupCaller(spec);

        const string payloadKey = WebScaffold.PAYLOAD_KEY;
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

        var ctx = await AppFileUploads.UploadEncryptedAsync(caller, spec.TargetDrive, fileMetadata, payloadDataRaw);

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
            Key = payloadKey,
            File = new ExternalFileIdentifier() { TargetDrive = targetDrive, FileId = fileId }
        });

        Assert.That(payloadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(payloadResponse.Content, Is.Not.Null);

        var payloadResponseCipher = await payloadResponse.Content!.ReadAsByteArrayAsync();
        Assert.That(ctx.PayloadCipher, Is.EqualTo(payloadResponseCipher));

        var aesKey = decryptedKeyHeader.AesKey;
        var decryptedPayloadBytes = AesCbc.Decrypt(
            cipherText: payloadResponseCipher,
            key: aesKey,
            iv: decryptedKeyHeader.Iv);

        var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payloadDataRaw);
        Assert.That(payloadBytes, Is.EqualTo(decryptedPayloadBytes));

        decryptedKeyHeader.AesKey.Wipe();
        ctx.KeyHeader.AesKey.Wipe();
    }

    [Test]
    public async Task CanSoftDeleteFile()
    {
        int SomeFileType = 194392901;

        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = false,
            IsEncrypted = true,
            AppData = new()
            {
                FileType = SomeFileType,
                Content = "{some:'file content'}",
            },
            AccessControlList = AccessControlList.OwnerOnly
        };

        var spec = CallerSpec.SampleAppWithConnectionReads();
        var caller = await SetupCaller(spec);

        var ctx = await AppFileUploads.UploadEncryptedAsync(caller, spec.TargetDrive, fileMetadata,
            payloadData: "this will be deleted", thumbnailSizes: [300]);

        Assert.That(ctx.Thumbnails.SingleOrDefault(), Is.Not.Null);
        var targetDrive = spec.TargetDrive;
        var fileToDelete = ctx.UploadResult.File;

        var svc = caller.RefitFor<IUniversalDriveHttpClientApi>();

        //validate the file is in the index
        var fileIsInIndexResponse = await svc.GetBatch(new QueryBatchRequest()
        {
            QueryParams = FileQueryParamsV1.FromFileType(targetDrive, SomeFileType),
            ResultOptionsRequest = new QueryBatchResultOptionsRequest()
            {
                MaxRecords = 10
            }
        });

        Assert.That(fileIsInIndexResponse?.Content?.SearchResults?.SingleOrDefault()?.FileMetadata?.AppData?.FileType,
            Is.EqualTo(SomeFileType));

        // delete the file
        var deleteFileResponse = await svc.SoftDeleteFile(new DeleteFileRequest() { File = fileToDelete });
        Assert.That(deleteFileResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(deleteFileResponse.Content, Is.Not.Null);
        Assert.That(deleteFileResponse.Content!.LocalFileDeleted, Is.True);

        //
        // Should still be in index
        //
        var qbResponse = await svc.GetBatch(new QueryBatchRequest()
        {
            QueryParams = FileQueryParamsV1.FromFileType(targetDrive),
            ResultOptionsRequest = new QueryBatchResultOptionsRequest()
            {
                MaxRecords = 10
            }
        });

        Assert.That(qbResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(qbResponse.Content, Is.Not.Null);
        var qbDeleteFileEntry = qbResponse.Content!.SearchResults.SingleOrDefault();
        OdinTestAssertions.FileHeaderIsMarkedDeleted(qbDeleteFileEntry);

        // crucial - it should return in query modified so apps can sync locally
        var queryModifiedResponse = await svc.GetModified(new QueryModifiedRequest()
        {
            QueryParams = FileQueryParamsV1.FromFileType(targetDrive, SomeFileType),
            ResultOptions = new QueryModifiedResultOptions()
            {
                MaxRecords = 10
            }
        });

        Assert.That(queryModifiedResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(queryModifiedResponse.Content, Is.Not.Null);
        var queryModifiedDeletedEntry = qbResponse.Content.SearchResults.SingleOrDefault();
        Assert.That(queryModifiedDeletedEntry, Is.Not.Null);
        OdinTestAssertions.FileHeaderIsMarkedDeleted(queryModifiedDeletedEntry);

        // get file directly
        var getFileHeaderResponse = await svc.GetFileHeaderAsPost(fileToDelete);
        Assert.That(getFileHeaderResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getFileHeaderResponse.Content, Is.Not.Null);
        var deletedFileHeader = getFileHeaderResponse.Content;
        OdinTestAssertions.FileHeaderIsMarkedDeleted(deletedFileHeader);

        //there should not be a thumbnail
        var thumb = ctx.Thumbnails[0].Descriptor;
        var getThumbnailResponse = await svc.GetThumbnailPost(new GetThumbnailRequest()
        {
            File = fileToDelete,
            Height = thumb.PixelHeight,
            Width = thumb.PixelWidth,
            PayloadKey = WebScaffold.PAYLOAD_KEY
        });
        Assert.That(getThumbnailResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        //there should not be a payload
        var getPayloadResponse = await svc.GetPayloadPost(new GetPayloadRequest() { File = fileToDelete, Key = WebScaffold.PAYLOAD_KEY });
        Assert.That(getPayloadResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    [Ignore("There is no api exposed for hard-delete for an App.")]
    public void CanHardDeleteFile()
    {
    }
}
