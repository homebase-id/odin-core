using System;
using System.Collections.Generic;
using System.IO;
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
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;
using Refit;
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
/// That is exactly <see cref="CallerSpec.App(DriveSpec, DrivePermission, IReadOnlyList{int})"/>, so
/// the port uses <c>SetupCaller</c>. One live caller, so no matrix and a plain <c>[Test]</c>. The
/// original's circle-with-drive creation is dropped: with no transit recipients nothing reads it.
/// <para>
/// The encrypted upload body moves to <see cref="AppFileUploads.UploadEncryptedAsync"/> (shared with
/// the ported <c>DriveQueryAppTests</c>); <see cref="UploadOnly"/> keeps its own hand-built
/// multipart body because its payload IV handling is the thing being round-tripped. Both go through
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
    /// <summary>The app <c>SetupTestSampleApp</c> built: all drive permissions, transit-write key, non-anonymous drive.</summary>
    private static CallerSpec SampleApp() =>
        CallerSpec.App(new DriveSpec(TargetDrive.NewTargetDrive(), AllowAnonymousReads: false),
            DrivePermission.All, [PermissionKeys.UseTransitWrite]);

    /// <summary>As <see cref="SampleApp"/> plus the connection-read keys the upload helper's app held.</summary>
    private static CallerSpec SampleAppWithConnectionReads() =>
        CallerSpec.App(new DriveSpec(TargetDrive.NewTargetDrive(), AllowAnonymousReads: false),
            DrivePermission.All,
            [PermissionKeys.ReadConnections, PermissionKeys.ReadConnectionRequests, PermissionKeys.UseTransitWrite]);

    [Test(Description = "Test Upload only; no expire, no drive; no transfer")]
    public async Task UploadOnly()
    {
        var spec = SampleApp();
        var caller = await SetupCaller(spec);

        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        const string payloadKey = WebScaffold.PAYLOAD_KEY;
        var payloadIv = ByteArrayUtil.GetRndByteArray(16);

        var instructionSet = new UploadInstructionSet()
        {
            TransferIv = transferIv,
            StorageOptions = new StorageOptions()
            {
                Drive = spec.TargetDrive
            },
            Manifest = new UploadManifest()
            {
                PayloadDescriptors = new List<UploadManifestPayloadDescriptor>()
                {
                    new()
                    {
                        Iv = payloadIv,
                        PayloadKey = payloadKey
                    }
                }
            }
        };

        var bytes = System.Text.Encoding.UTF8.GetBytes(OdinSystemSerializer.Serialize(instructionSet));
        var instructionStream = new MemoryStream(bytes);

        var client = caller.Factory.CreateHttpClient(caller.Identity, out var sba);
        var descriptor = new UploadFileDescriptor()
        {
            EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref sba),
            FileMetadata = new()
            {
                AllowDistribution = false,
                IsEncrypted = true,
                AppData = new()
                {
                    Tags = new List<Guid>() { Guid.NewGuid(), Guid.NewGuid() },
                    Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" })
                }
            },
        };

        var key = sba;
        var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref key);

        var payloadDataRaw = "{payload:true, image:'b64 data'}";
        var payloadCipher = keyHeader.EncryptDataAesAsStream(payloadDataRaw);

        {
            var svc = RestService.For<IUniversalDriveHttpClientApi>(client);
            var response = await svc.UploadStream(
            [
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                new StreamPart(payloadCipher, payloadKey, "application/x-binary", Enum.GetName(MultipartUploadParts.Payload))
            ]);

            Assert.That(response.IsSuccessStatusCode, Is.True);
            Assert.That(response.Content, Is.Not.Null);
            var uploadResult = response.Content;

            Assert.That(uploadResult!.File, Is.Not.Null);
            Assert.That(uploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(uploadResult.File.TargetDrive.IsValid(), Is.True);

            // Assert.That(uploadResult.RecipientStatus, Is.Not.Null);
            // ClassicAssert.IsTrue(uploadResult.RecipientStatus.Count == 0, "Too many recipient results returned");

            //

            ////
            var targetDrive = uploadResult.File.TargetDrive;
            var fileId = uploadResult.File.FileId;

            //retrieve the file that was uploaded; decrypt;
            var driveSvc = RefitCreator.RestServiceFor<IUniversalDriveHttpClientApi>(client, sba);

            var fileResponse = await driveSvc.GetFileHeaderAsPost(new ExternalFileIdentifier() { TargetDrive = targetDrive, FileId = fileId });

            Assert.That(fileResponse.IsSuccessStatusCode, Is.True);
            Assert.That(fileResponse.Content, Is.Not.Null);

            var clientFileHeader = fileResponse.Content;

            Assert.That(clientFileHeader!.FileMetadata, Is.Not.Null);
            Assert.That(clientFileHeader.FileMetadata.AppData, Is.Not.Null);

            Assert.That(clientFileHeader.FileMetadata.AppData.Tags, Is.EquivalentTo(descriptor.FileMetadata.AppData.Tags));
            Assert.That(clientFileHeader.FileMetadata.AppData.Content, Is.EqualTo(descriptor.FileMetadata.AppData.Content));
            Assert.That(clientFileHeader.FileMetadata.Payloads.Count, Is.EqualTo(1));
            Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader, Is.Not.Null);
            Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.Null);
            Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv.Length, Is.GreaterThanOrEqualTo(16));
            Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.EqualTo(Guid.Empty.ToByteArray()), "Iv was all zeros");
            Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.Aes));

            var decryptedKeyHeader = clientFileHeader.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref key);

            Assert.That(decryptedKeyHeader.AesKey.IsSet(), Is.True);
            var fileKey = decryptedKeyHeader.AesKey;
            Assert.That(fileKey, Is.Not.EqualTo(Guid.Empty.ToByteArray()));

            //get the payload and decrypt, then compare
            var payloadResponse = await driveSvc.GetPayloadPost(new GetPayloadRequest()
            {
                Key = payloadKey,
                File = new ExternalFileIdentifier() { TargetDrive = targetDrive, FileId = fileId }
            });

            Assert.That(payloadResponse.IsSuccessStatusCode, Is.True);
            Assert.That(payloadResponse.Content, Is.Not.Null);

            var payloadResponseCipher = await payloadResponse.Content!.ReadAsByteArrayAsync();
            Assert.That(((MemoryStream)payloadCipher).ToArray(), Is.EqualTo(payloadResponseCipher));

            var aesKey = decryptedKeyHeader.AesKey;
            var decryptedPayloadBytes = AesCbc.Decrypt(
                cipherText: payloadResponseCipher,
                key: aesKey,
                iv: decryptedKeyHeader.Iv);

            var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payloadDataRaw);
            Assert.That(payloadBytes, Is.EqualTo(decryptedPayloadBytes));

            decryptedKeyHeader.AesKey.Wipe();
        }

        keyHeader.AesKey.Wipe();
    }

    [Test(Description = "")]
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

        var spec = SampleAppWithConnectionReads();
        var caller = await SetupCaller(spec);

        var ctx = await AppFileUploads.UploadEncryptedAsync(caller, spec.TargetDrive, fileMetadata,
            payloadData: "this will be deleted", includeThumbnail: true);

        Assert.That(ctx.Thumbnails.SingleOrDefault(), Is.Not.Null);
        var targetDrive = spec.TargetDrive;
        var fileToDelete = ctx.UploadResult.File;

        var client = caller.Factory.CreateHttpClient(caller.Identity, out var sharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IUniversalDriveHttpClientApi>(client, sharedSecret);

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
            Assert.That(deleteFileResponse.IsSuccessStatusCode, Is.True);
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

            Assert.That(qbResponse.IsSuccessStatusCode, Is.True);
            Assert.That(qbResponse.Content, Is.Not.Null);
            var qbDeleteFileEntry = qbResponse.Content!.SearchResults.SingleOrDefault();
            OdinTestAssertions.FileHeaderIsMarkedDeleted(qbDeleteFileEntry);

            await Task.Delay(5);

            // crucial - it should return in query modified so apps can sync locally
            var queryModifiedResponse = await svc.GetModified(new QueryModifiedRequest()
            {
                QueryParams = FileQueryParamsV1.FromFileType(targetDrive, SomeFileType),
                ResultOptions = new QueryModifiedResultOptions()
                {
                    MaxRecords = 10
                }
            });

            Assert.That(queryModifiedResponse.IsSuccessStatusCode, Is.True);
            Assert.That(queryModifiedResponse.Content, Is.Not.Null);
            var queryModifiedDeletedEntry = qbResponse.Content.SearchResults.SingleOrDefault();
            Assert.That(queryModifiedDeletedEntry, Is.Not.Null);
            OdinTestAssertions.FileHeaderIsMarkedDeleted(queryModifiedDeletedEntry);

            // get file directly
            var getFileHeaderResponse = await svc.GetFileHeaderAsPost(fileToDelete);
            Assert.That(getFileHeaderResponse.IsSuccessStatusCode, Is.True);
            Assert.That(getFileHeaderResponse.Content, Is.Not.Null);
            var deletedFileHeader = getFileHeaderResponse.Content;
            OdinTestAssertions.FileHeaderIsMarkedDeleted(deletedFileHeader);

            //there should not be a thumbnail
            var thumb = ctx.Thumbnails.FirstOrDefault();
            var getThumbnailResponse = await svc.GetThumbnailPost(new GetThumbnailRequest()
            {
                File = fileToDelete,
                Height = thumb!.PixelHeight,
                Width = thumb.PixelWidth,
                PayloadKey = WebScaffold.PAYLOAD_KEY
            });
            Assert.That(getThumbnailResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

            //there should not be a payload
            var getPayloadResponse = await svc.GetPayloadPost(new GetPayloadRequest() { File = fileToDelete, Key = WebScaffold.PAYLOAD_KEY });
            Assert.That(getPayloadResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }
    }

    [Test(Description = "")]
    [Ignore("There is no api exposed for hard-delete for an App.")]
    public void CanHardDeleteFile()
    {
    }
}
