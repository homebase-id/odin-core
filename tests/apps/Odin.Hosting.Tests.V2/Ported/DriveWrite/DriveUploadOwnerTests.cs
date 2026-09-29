using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Tests;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;

namespace Odin.Hosting.Tests.V2.Ported.DriveWrite;

/// <summary>
/// Port of <c>OwnerApi/Drive/StandardFileSystem/DriveUploadOwnerTests</c>. The owner's V1 upload
/// surface: an encrypted metadata-only upload round-trips through the header endpoint; an encrypted
/// file cannot be uploaded with an Anonymous required-security-group; an upload with two thumbnails
/// round-trips payload, preview thumbnail and both thumbnails; overwriting a file id that does not
/// exist is refused; and the client-unique-id rules (queryable by unique id, no two live files
/// sharing one, cannot update a file onto another's unique id).
/// </summary>
/// <remarks>
/// No caller matrix — an <c>OwnerApi</c> fixture, so plain <c>[Test]</c> methods and
/// <c>LoginAsOwner</c>; the <c>SetupCallerWithOwner</c> ordering caveat does not apply.
/// <para>
/// <b>Arrange rewrite.</b> Six of these tests arranged their drive with
/// <c>_scaffold.OldOwnerApi.SetupTestSampleApp(...)</c>, which creates a drive <i>and</i> registers
/// an app and an app client. The acting caller in every one of them is the <b>owner</b> — the app
/// registration is never used — so the port creates just the drive via <c>owner.Admin.CreateDrive</c>
/// with the same flags <c>SetupTestSampleApp</c> passed (<c>AllowAnonymousReads: false</c>, and
/// <c>OwnerOnly: true</c> for the one test that asked for an owner-only drive). The drive's name and
/// metadata differ from <c>SetupTestSampleApp</c>'s (<c>"Test Drive name with type {type}"</c> /
/// <c>"{data:'test metadata'}"</c>); no assertion in this fixture reads either.
/// </para>
/// <para>
/// <b>Identities.</b> The original booted Frodo, Samwise and Merry and pinned one per test, because
/// <c>WebScaffold</c> shares identities process-wide and unique-id collisions would leak between
/// tests. Here each test gets a freshly-restored tenant and its own drive, so all of them run on the
/// fixture default.
/// </para>
/// <para>
/// The raw-multipart tests send their bodies through <see cref="AppFileUploads"/>, which builds the
/// same parts they used to build by hand and posts them to <see cref="IUniversalDriveHttpClientApi"/>
/// (same V1 <c>/drive/files/upload</c> endpoint the original's <c>IDriveTestHttpClientForOwner</c>
/// addressed, resolved against the owner's V1 base by the factory's path handler). One naming
/// difference: the thumbnail part keys are now <c>UploadExtensions.GetFilename</c>'s
/// <c>"{w}x{h}-{payloadKey}"</c> rather than the original's <c>"{h}{w}{payloadKey}"</c>. The key only
/// has to pair a part with its manifest entry inside one request, and both sides use the same string;
/// thumbnails are fetched back by pixel size, which is unchanged.
/// The original's trailing <c>ownerSharedSecret.Wipe()</c> in
/// <see cref="UploadWithThumbnails"/> is dropped: here that byte array belongs to the
/// <see cref="OwnerSession"/>, not to the test. Locally-created key headers are still wiped.
/// </para>
/// </remarks>
[TestFixture]
public class DriveUploadOwnerTests : V2Fixture
{
    [Test]
    [Ignore("This is tested in the app api until we determine if there are diff behaviors when transferring using the owner api")]
    public void CanGetAndSetGlobalTransitId()
    {
    }

    [Test(Description = "Test upload as owner")]
    public async Task UploadOnly()
    {
        var frodoOwnerClient = await LoginAsOwner();

        var targetDrive = TargetDrive.NewTargetDrive();
        await frodoOwnerClient.Admin.CreateDrive(targetDrive, "some drive", allowAnonymousReads: false, ownerOnly: true);

        var metadata = new UploadFileMetadata()
        {
            IsEncrypted = true,
            AllowDistribution = false,
            AppData = new()
            {
                Tags = new List<Guid>() { Guid.NewGuid(), Guid.NewGuid() },
                Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" })
            }
        };

        var (uploadResponse, _) = await frodoOwnerClient.V1.Drive.UploadNewEncryptedMetadata(targetDrive, metadata);

        var uploadResult = uploadResponse.Content;

        Assert.That(uploadResult!.File, Is.Not.Null);
        Assert.That(uploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(uploadResult.File.TargetDrive.IsValid(), Is.True);
        Assert.That(uploadResult.File.TargetDrive, Is.EqualTo(targetDrive));
        Assert.That(uploadResult.RecipientStatus, Is.Null);

        var fileId = uploadResult.File.FileId;

        //retrieve the file that was uploaded; decrypt;
        var clientFileHeader = (await frodoOwnerClient.V1.Drive.GetFileHeader(
            new ExternalFileIdentifier() { TargetDrive = targetDrive, FileId = fileId },
            FileSystemType.Standard)).Content;

        Assert.That(clientFileHeader!.FileMetadata, Is.Not.Null);
        Assert.That(clientFileHeader.FileMetadata.AppData, Is.Not.Null);

        Assert.That(clientFileHeader.FileMetadata.AppData.Tags, Is.EquivalentTo(metadata.AppData.Tags));
        Assert.That(clientFileHeader.FileMetadata.AppData.Content, Is.EqualTo(metadata.AppData.Content));
        Assert.That(clientFileHeader.FileMetadata.Payloads.Count, Is.EqualTo(0));

        Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader, Is.Not.Null);
        Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.Null);
        Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv.Length, Is.GreaterThanOrEqualTo(16));
        Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Iv, Is.Not.EqualTo(Guid.Empty.ToByteArray()), "Iv was all zeros");
        Assert.That(clientFileHeader.SharedSecretEncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.Aes));

        Assert.That(clientFileHeader.FileByteCount, Is.GreaterThan(0), "Disk usage was not calculated");
    }

    [Test(Description = "Test upload as owner")]
    public async Task FailsToUploadInvalidRequiredSecurityGroupToOwnerOnlyDrive()
    {
        var owner = await LoginAsOwner();
        var targetDrive = await CreateSampleDrive(owner, ownerOnly: true);

        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = false,
            IsEncrypted = true,
            AppData = new()
            {
                Tags = new List<Guid>() { Guid.NewGuid(), Guid.NewGuid() },
                Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" })
            },
            AccessControlList = new() { RequiredSecurityGroup = SecurityGroupType.Anonymous }
        };

        var ctx = await AppFileUploads.TryUploadEncryptedAsync(owner, targetDrive, fileMetadata);

        Assert.That(ctx.Response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var code = TestUtils.ParseProblemDetails(ctx.Response.Error!);
        Assert.That(code, Is.EqualTo(OdinClientErrorCode.CannotUploadEncryptedFileForAnonymous));

        ctx.KeyHeader.AesKey.Wipe();
    }

    [Test(Description = "Test upload thumbnails as owner")]
    public async Task UploadWithThumbnails()
    {
        var owner = await LoginAsOwner();
        var targetDrive = await CreateSampleDrive(owner);

        var keyHeader = KeyHeader.NewRandom16();
        const string payloadDataRaw = "{payload:true, image:'b64 data'}";

        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = false,
            IsEncrypted = true,
            AppData = new()
            {
                Tags = new List<Guid>() { Guid.NewGuid(), Guid.NewGuid() },
                Content = OdinSystemSerializer.Serialize(new { content = "some content" }),

                PreviewThumbnail = new ThumbnailContent()
                {
                    PixelHeight = 100,
                    PixelWidth = 100,
                    ContentType = "image/png",
                    Content = keyHeader.EncryptDataAes(TestMedia.PreviewPngThumbnailBytes)
                }
            }
        };

        var ctx = await AppFileUploads.UploadEncryptedAsync(owner, targetDrive, fileMetadata, payloadDataRaw,
            thumbnailSizes: [300, 400], keyHeader: keyHeader);

        Assert.That(ctx.UploadResult.RecipientStatus, Is.Null);
        var uploadedFile = ctx.UploadResult.File;

        //
        // Retrieve the file header that was uploaded; test it matches;
        //
        var client = owner.Factory.CreateHttpClient(owner.Identity, out var ownerSharedSecret);
        var getFilesDriveSvc = RefitCreator.RestServiceFor<IUniversalDriveHttpClientApi>(client, ownerSharedSecret);
        var fileResponse = await getFilesDriveSvc.GetFileHeaderAsPost(uploadedFile);

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

        var decryptedKeyHeader = clientFileHeader.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref ownerSharedSecret);

        Assert.That(decryptedKeyHeader.AesKey.IsSet(), Is.True);
        Assert.That(ByteArrayUtil.EquiByteArrayCompare(decryptedKeyHeader.AesKey.GetKey(), keyHeader.AesKey.GetKey()), Is.True);

        //validate preview thumbnail
        Assert.That(clientFileHeader.FileMetadata.AppData.PreviewThumbnail.ContentType,
            Is.EqualTo(fileMetadata.AppData.PreviewThumbnail.ContentType));
        Assert.That(clientFileHeader.FileMetadata.AppData.PreviewThumbnail.PixelHeight,
            Is.EqualTo(fileMetadata.AppData.PreviewThumbnail.PixelHeight));
        Assert.That(clientFileHeader.FileMetadata.AppData.PreviewThumbnail.PixelWidth,
            Is.EqualTo(fileMetadata.AppData.PreviewThumbnail.PixelWidth));
        Assert.That(ByteArrayUtil.EquiByteArrayCompare(fileMetadata.AppData.PreviewThumbnail.Content,
            clientFileHeader.FileMetadata.AppData.PreviewThumbnail.Content), Is.True);

        Assert.That(clientFileHeader.FileMetadata.GetPayloadDescriptor(WebScaffold.PAYLOAD_KEY).Thumbnails.Count(), Is.EqualTo(2));

        //
        // Get the payload that was uploaded, test it
        //

        var payloadResponse = await getFilesDriveSvc.GetPayloadPost(new GetPayloadRequest() { File = uploadedFile, Key = WebScaffold.PAYLOAD_KEY });
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

        //
        // Validate additional thumbnails
        //

        var clientFileHeaderList = clientFileHeader.FileMetadata.GetPayloadDescriptor(WebScaffold.PAYLOAD_KEY).Thumbnails.ToList();

        for (var i = 0; i < ctx.Thumbnails.Count; i++)
        {
            var (thumbnail, thumbnailCipherBytes) = ctx.Thumbnails[i];

            Assert.That(clientFileHeaderList[i].ContentType, Is.EqualTo(thumbnail.ContentType));
            Assert.That(clientFileHeaderList[i].PixelWidth, Is.EqualTo(thumbnail.PixelWidth));
            Assert.That(clientFileHeaderList[i].PixelHeight, Is.EqualTo(thumbnail.PixelHeight));

            var thumbnailResponse = await getFilesDriveSvc.GetThumbnailPost(new GetThumbnailRequest()
            {
                File = uploadedFile,
                Height = thumbnail.PixelHeight,
                Width = thumbnail.PixelWidth,
                PayloadKey = WebScaffold.PAYLOAD_KEY
            });

            Assert.That(thumbnailResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(thumbnailResponse.Content, Is.Not.Null);
            Assert.That(ByteArrayUtil.EquiByteArrayCompare(thumbnailCipherBytes,
                await thumbnailResponse.Content!.ReadAsByteArrayAsync()), Is.True);
        }

        decryptedKeyHeader.AesKey.Wipe();
        keyHeader.AesKey.Wipe();
    }

    //tests
    [Test]
    public async Task FailToUpdateNonExistentFile()
    {
        var owner = await LoginAsOwner();
        var targetDrive = await CreateSampleDrive(owner);

        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = false,
            IsEncrypted = true,
            AppData = new()
            {
                Tags = new List<Guid>() { Guid.NewGuid(), Guid.NewGuid() },
                Content = "some content"
            }
        };

        var ctx = await AppFileUploads.TryUploadEncryptedAsync(owner, targetDrive, fileMetadata,
            overwriteFileId: Guid.NewGuid()); //some random guid pretending to be a file that exists

        Assert.That(ctx.Response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var code = TestUtils.ParseProblemDetails(ctx.Response.Error!);
        Assert.That(code, Is.EqualTo(OdinClientErrorCode.CannotOverwriteNonExistentFile));

        ctx.KeyHeader.AesKey.Wipe();
    }

    [Test]
    public async Task CanUploadClientUniqueIdAndGetOneFile()
    {
        //(use query modified and querybatch)

        var ownerClient = await LoginAsOwner();
        var targetDrive = await CreateSampleDrive(ownerClient);

        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = false,
            IsEncrypted = true,
            AppData = new()
            {
                UniqueId = Guid.NewGuid(),
                Tags = new List<Guid>() { Guid.NewGuid(), Guid.NewGuid() },
                Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" })
            }
        };

        var payloadDataRaw = "{payload:true, image:'b64 data'}";

        var testPayloads = new List<TestPayloadDefinition>()
        {
            new()
            {
                Iv = ByteArrayUtil.GetRndByteArray(16),
                Key = WebScaffold.PAYLOAD_KEY,
                ContentType = "text/plain",
                Content = payloadDataRaw.ToUtf8ByteArray(),
                Thumbnails = new List<ThumbnailContent>() { }
            }
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var (response, _, _, _) = await ownerClient.V1.Drive.UploadNewEncryptedFile(targetDrive, KeyHeader.NewRandom16(),
            fileMetadata, uploadManifest, testPayloads);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content, Is.Not.Null);
        var uploadResult = response.Content;

        Assert.That(uploadResult!.File, Is.Not.Null);
        Assert.That(uploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(uploadResult.File.TargetDrive.IsValid(), Is.True);

        //
        // Get file bu ClientUniqueId
        //

        var expectedClientUniqueId = fileMetadata.AppData.UniqueId.GetValueOrDefault();
        var qp = new FileQueryParamsV1()
        {
            TargetDrive = uploadResult.File.TargetDrive,
            ClientUniqueIdAtLeastOne = new List<Guid>() { expectedClientUniqueId }
        };

        var resultOptions = new QueryBatchResultOptionsRequest()
        {
            CursorState = "",
            MaxRecords = 10,
            IncludeMetadataHeader = false
        };

        var getBatchResponse = await ownerClient.V1.Drive.QueryBatch(new QueryBatchRequest()
        {
            QueryParams = qp,
            ResultOptionsRequest = resultOptions
        });

        Assert.That(getBatchResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var batch = getBatchResponse.Content;

        Assert.That(batch, Is.Not.Null);
        Assert.That(batch!.SearchResults.Single(item => item.FileMetadata.AppData.UniqueId == expectedClientUniqueId), Is.Not.Null);
    }

    [Test]
    public async Task FailToUploadTwoFilesWithSameClientUniqueId()
    {
        var uid1 = Guid.NewGuid();
        var client2 = await LoginAsOwner();
        var targetDrive = await UploadUniqueIdTestFile(client2, uid1);

        //
        // Upload a new file and try using uid1, which is already in use by file1
        //

        var f2 = new UploadFileMetadata()
        {
            AllowDistribution = false,
            IsEncrypted = true,
            AppData = new()
            {
                UniqueId = uid1,
                Content = OdinSystemSerializer.Serialize(new { message = "I am a second file" })
            }
        };

        var response = await client2.V1.Drive.UploadNewMetadata(targetDrive, f2);
        //
        // This should fail because we tried to reuse a uid1
        //
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var code = TestUtils.ParseProblemDetails(response.Error!);
        Assert.That(code, Is.EqualTo(OdinClientErrorCode.ExistingFileWithUniqueId));
    }

    [Test]
    public async Task FailToChangeClientUniqueIdToExistingClientUniqueId()
    {
        var uid1 = Guid.NewGuid();
        var uid2 = Guid.NewGuid();

        var client = await LoginAsOwner();
        var targetDrive = await UploadUniqueIdTestFile(client, uid1);

        //
        // Upload a second file to the same drive with uid2
        //
        var fileMetadata2 = new UploadFileMetadata()
        {
            AllowDistribution = false,
            IsEncrypted = true,
            AppData = new()
            {
                UniqueId = uid2,
                Content = OdinSystemSerializer.Serialize(new { message = "I am a second file" })
            }
        };

        var response2 = await client.V1.Drive.UploadNewMetadata(targetDrive, fileMetadata2);
        Assert.That(response2.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response2.Content, Is.Not.Null);

        UploadResult secondFileUploadResult = response2.Content!;
        Assert.That(secondFileUploadResult.File, Is.Not.Null);
        Assert.That(secondFileUploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(secondFileUploadResult.File.TargetDrive.IsValid(), Is.True);

        //
        // Update second file and try using uid1, which is already in use by file1
        //
        var fileMetadata3 = new UploadFileMetadata()
        {
            AllowDistribution = false,
            IsEncrypted = true,
            VersionTag = secondFileUploadResult.NewVersionTag,
            AppData = new()
            {
                UniqueId = uid1,
                Content = OdinSystemSerializer.Serialize(new { message = "Some message" })
            }
        };

        var response3 = await client.V1.Drive.UpdateExistingMetadata(secondFileUploadResult.File,
            secondFileUploadResult.NewVersionTag, fileMetadata3);

        Assert.That(response3.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var code = TestUtils.ParseProblemDetails(response3.Error!);
        Assert.That(code, Is.EqualTo(OdinClientErrorCode.ExistingFileWithUniqueId));
    }

    /// <summary>
    /// Uploads one file carrying <paramref name="uniqueId"/> onto a fresh drive and proves it comes
    /// back from query-batch by that unique id. Returns the drive so the caller can keep using it.
    /// </summary>
    /// <remarks>
    /// The original returned the <c>TestAppContext</c> from <c>SetupTestSampleApp</c> and a
    /// <c>UploadResult</c> neither caller read; only the context's <c>TargetDrive</c> was used.
    /// </remarks>
    private static async Task<TargetDrive> UploadUniqueIdTestFile(OwnerSession client, Guid? uniqueId)
    {
        var targetDrive = await CreateSampleDrive(client);

        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = false,
            IsEncrypted = true,
            AppData = new()
            {
                UniqueId = uniqueId,
                Content = OdinSystemSerializer.Serialize(new { message = "Some message" })
            }
        };

        var response = await client.V1.Drive.UploadNewMetadata(targetDrive, fileMetadata);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content, Is.Not.Null);
        var uploadResult = response.Content;

        Assert.That(uploadResult!.File, Is.Not.Null);
        Assert.That(uploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(uploadResult.File.TargetDrive.IsValid(), Is.True);

        //
        // Get file by ClientUniqueId
        //

        var qp = new FileQueryParamsV1()
        {
            TargetDrive = uploadResult.File.TargetDrive,
            ClientUniqueIdAtLeastOne = new List<Guid>() { uniqueId.GetValueOrDefault() }
        };

        var resultOptions = new QueryBatchResultOptionsRequest()
        {
            CursorState = "",
            MaxRecords = 10,
            IncludeMetadataHeader = false
        };

        var getBatchResponse = await client.V1.Drive.QueryBatch(new QueryBatchRequest()
        {
            QueryParams = qp,
            ResultOptionsRequest = resultOptions
        });

        Assert.That(getBatchResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var batch = getBatchResponse.Content;

        Assert.That(batch, Is.Not.Null);
        Assert.That(batch!.SearchResults.Single(item => item.FileMetadata.AppData.UniqueId == uniqueId.GetValueOrDefault()), Is.Not.Null);

        return targetDrive;
    }

    /// <summary>
    /// The drive <c>OwnerApiTestUtils.SetupTestSampleApp</c> created for these tests — anonymous
    /// reads off, optionally owner-only. The app registration that came with it is dropped: every
    /// caller in this fixture is the owner.
    /// </summary>
    private static async Task<TargetDrive> CreateSampleDrive(OwnerSession owner, bool ownerOnly = false)
    {
        var targetDrive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(targetDrive, $"Test Drive name with type {targetDrive.Type}",
            allowAnonymousReads: false, ownerOnly: ownerOnly);
        return targetDrive;
    }
}
