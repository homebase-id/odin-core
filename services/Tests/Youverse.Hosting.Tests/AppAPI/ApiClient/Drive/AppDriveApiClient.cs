using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.DriveCore.Query;
using Youverse.Core.Services.Drives.DriveCore.Storage;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.SendingHost;
using Youverse.Core.Storage;
using Youverse.Hosting.Controllers.OwnerToken.Drive;
using Youverse.Hosting.Tests.AppAPI.Drive;
using Youverse.Hosting.Tests.AppAPI.Utils;
using Youverse.Hosting.Tests.OwnerApi.ApiClient;
using Youverse.Hosting.Tests.OwnerApi.Utils;

namespace Youverse.Hosting.Tests.AppAPI.ApiClient.Drive;

public class AppDriveApiClient : AppApiTestUtils
{
    private readonly AppClientToken _token;

    public AppDriveApiClient(OwnerApiTestUtils ownerApiTestUtils, AppClientToken token) : base(ownerApiTestUtils)
    {
        _token = token;
    }

    public async Task<SharedSecretEncryptedFileHeader> QueryByGlobalTransitFileId(FileSystemType fileSystemType, GlobalTransitIdFileIdentifier file)
    {
        var batch = await this.QueryBatch(fileSystemType, new FileQueryParams()
        {
            TargetDrive = file.TargetDrive,
            GlobalTransitId = new List<Guid>() { file.GlobalTransitId }
        }, new QueryBatchResultOptionsRequest()
        {
            MaxRecords = 10,
            IncludeMetadataHeader = true
        });

        return batch.SearchResults.SingleOrDefault();
    }

    public async Task<QueryBatchResponse> QueryBatch(FileSystemType fileSystemType, FileQueryParams qp, QueryBatchResultOptionsRequest resultOptions = null)
    {
        using (var client = CreateAppApiHttpClient(_token, fileSystemType))
        {
            var svc = CreateDriveService(client);

            var ro = resultOptions ?? new QueryBatchResultOptionsRequest()
            {
                CursorState = "",
                MaxRecords = 10,
                IncludeMetadataHeader = true
            };

            var request = new QueryBatchRequest()
            {
                QueryParams = qp,
                ResultOptionsRequest = ro
            };

            var response = await svc.GetBatch(request);
            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            var batch = response.Content;
            Assert.IsNotNull(batch);

            return batch;
        }
    }

    public async Task<UploadResult> UploadFile(FileSystemType fileSystemType, TargetDrive targetDrive, UploadFileMetadata fileMetadata,
        string payloadData = "",
        ImageDataContent thumbnail = null,
        Guid? overwriteFileId = null)
    {
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        UploadInstructionSet instructionSet = new UploadInstructionSet()
        {
            TransferIv = transferIv,
            StorageOptions = new()
            {
                Drive = targetDrive,
                OverwriteFileId = overwriteFileId
            },
            TransitOptions = new TransitOptions()
            {
                UseGlobalTransitId = true
            }
        };

        using (var client = CreateAppApiHttpClient(_token, fileSystemType))
        {
            var sharedSecret = _token.SharedSecret.ToSensitiveByteArray();

            var instructionStream = new MemoryStream(DotYouSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

            fileMetadata.PayloadIsEncrypted = false;

            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, instructionSet.TransferIv, ref sharedSecret),
                FileMetadata = fileMetadata
            };

            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, instructionSet.TransferIv, ref sharedSecret);

            List<StreamPart> parts = new()
            {
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                new StreamPart(new MemoryStream(payloadData.ToUtf8ByteArray()), "payload.encrypted", "application/x-binary",
                    Enum.GetName(MultipartUploadParts.Payload))
            };

            if (thumbnail != null)
            {
                var thumbnailCipherBytes = keyHeader.EncryptDataAesAsStream(thumbnail.Content);
                parts.Add(new StreamPart(thumbnailCipherBytes, thumbnail.GetFilename(), thumbnail.ContentType, Enum.GetName(MultipartUploadParts.Thumbnail)));
            }

            var driveSvc = RestService.For<IDriveTestHttpClientForApps>(client);
            ApiResponse<UploadResult> response = await driveSvc.Upload(parts.ToArray());

            Assert.That(response.IsSuccessStatusCode, Is.True);
            Assert.That(response.Content, Is.Not.Null);
            var uploadResult = response.Content;

            Assert.That(uploadResult.File, Is.Not.Null);
            Assert.That(uploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(uploadResult.File.TargetDrive, Is.Not.EqualTo(Guid.Empty));

            keyHeader.AesKey.Wipe();

            return uploadResult;
        }
    }

    public async Task<(UploadResult uploadResult, string encryptedJsonContent64)> UploadEncryptedFile(FileSystemType fileSystemType, TargetDrive targetDrive,
        UploadFileMetadata fileMetadata,
        string payloadData = "",
        ImageDataContent thumbnail = null,
        Guid? overwriteFileId = null)
    {
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        UploadInstructionSet instructionSet = new UploadInstructionSet()
        {
            TransferIv = transferIv,
            StorageOptions = new()
            {
                Drive = targetDrive,
                OverwriteFileId = overwriteFileId
            },
            TransitOptions = new TransitOptions()
            {
                UseGlobalTransitId = true
            }
        };

        using (var client = CreateAppApiHttpClient(_token, fileSystemType))
        {
            var sharedSecret = _token.SharedSecret.ToSensitiveByteArray();

            var instructionStream = new MemoryStream(DotYouSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

            var encryptedJsonContent64 = keyHeader.EncryptDataAes(fileMetadata.AppData.JsonContent.ToUtf8ByteArray()).ToBase64();
            fileMetadata.AppData.JsonContent = encryptedJsonContent64;
            fileMetadata.PayloadIsEncrypted = true;

            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, instructionSet.TransferIv, ref sharedSecret),
                FileMetadata = fileMetadata
            };

            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, instructionSet.TransferIv, ref sharedSecret);

            //expect a payload if the caller says there should be one
            byte[] encryptedPayloadBytes = Array.Empty<byte>();
            if (fileMetadata.AppData.ContentIsComplete == false)
            {
                encryptedPayloadBytes = keyHeader.EncryptDataAes(payloadData.ToUtf8ByteArray());
            }

            List<StreamPart> parts = new()
            {
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                new StreamPart(new MemoryStream(encryptedPayloadBytes), "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload))
            };

            if (thumbnail != null)
            {
                var thumbnailCipherBytes = keyHeader.EncryptDataAesAsStream(thumbnail.Content);
                parts.Add(new StreamPart(thumbnailCipherBytes, thumbnail.GetFilename(), thumbnail.ContentType, Enum.GetName(MultipartUploadParts.Thumbnail)));
            }

            var driveSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
            ApiResponse<UploadResult> response = await driveSvc.UploadStream(parts.ToArray());

            Assert.That(response.IsSuccessStatusCode, Is.True);
            Assert.That(response.Content, Is.Not.Null);
            var uploadResult = response.Content;

            Assert.That(uploadResult.File, Is.Not.Null);
            Assert.That(uploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(uploadResult.File.TargetDrive, Is.Not.EqualTo(Guid.Empty));

            keyHeader.AesKey.Wipe();

            return (uploadResult, encryptedJsonContent64);
        }
    }

    public async Task<SharedSecretEncryptedFileHeader> GetFileHeader(FileSystemType fileSystemType, ExternalFileIdentifier file)
    {
        using (var client = CreateAppApiHttpClient(_token, fileSystemType))
        {
            //wth - refit is not sending headers when you do GET request - why not!?
            var svc = CreateDriveService(client);
            // var apiResponse = await svc.GetFileHeader(file.FileId, file.TargetDrive.Alias, file.TargetDrive.Type);
            var apiResponse = await svc.GetFileHeaderAsPost(file);
            return apiResponse.Content;
        }
    }

    public async Task DeleteFile(FileSystemType fileSystemType, ExternalFileIdentifier file, List<string> recipients = null)
    {
        using (var client = CreateAppApiHttpClient(_token, fileSystemType))
        {
            var svc = CreateDriveService(client);
            // var apiResponse = await svc.GetFileHeader(file.FileId, file.TargetDrive.Alias, file.TargetDrive.Type);
            var apiResponse = await svc.DeleteFile(new DeleteFileRequest()
            {
                File = file,
                Recipients = recipients
            });
        }
    }

    /// <summary>
    /// Uploads the file to the senders drive then sends it to the recipients
    /// </summary>
    public async Task<UploadResult> UploadAndTransferFile(FileSystemType fileSystemType,
        UploadFileMetadata fileMetadata,
        StorageOptions storageOptions,
        TransitOptions transitOptions,
        string payloadData = "",
        ImageDataContent thumbnail = null
    )
    {
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        UploadInstructionSet instructionSet = new UploadInstructionSet()
        {
            TransferIv = transferIv,
            StorageOptions = storageOptions,
            TransitOptions = transitOptions
        };

        using (var client = CreateAppApiHttpClient(_token, fileSystemType))
        {
            var sharedSecret = _token.SharedSecret.ToSensitiveByteArray();

            var instructionStream = new MemoryStream(DotYouSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

            // fileMetadata.AppData.JsonContent = keyHeader.EncryptDataAes(fileMetadata.AppData.JsonContent.ToUtf8ByteArray()).ToBase64();
            fileMetadata.PayloadIsEncrypted = false;

            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, instructionSet.TransferIv, ref sharedSecret),
                FileMetadata = fileMetadata
            };

            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, instructionSet.TransferIv, ref sharedSecret);

            List<StreamPart> parts = new()
            {
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                new StreamPart(new MemoryStream(payloadData.ToUtf8ByteArray()), "payload.encrypted", "application/x-binary",
                    Enum.GetName(MultipartUploadParts.Payload))
            };

            if (thumbnail != null)
            {
                var thumbnailCipherBytes = keyHeader.EncryptDataAesAsStream(thumbnail.Content);
                parts.Add(new StreamPart(thumbnailCipherBytes, thumbnail.GetFilename(), thumbnail.ContentType, Enum.GetName(MultipartUploadParts.Thumbnail)));
            }

            var driveSvc = CreateDriveService(client);
            ApiResponse<UploadResult> response = await driveSvc.Upload(parts.ToArray());

            Assert.That(response.IsSuccessStatusCode, Is.True);
            Assert.That(response.Content, Is.Not.Null);
            var uploadResult = response.Content;

            Assert.That(uploadResult.File, Is.Not.Null);
            Assert.That(uploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(uploadResult.File.TargetDrive, Is.Not.EqualTo(Guid.Empty));

            keyHeader.AesKey.Wipe();

            return uploadResult;
        }
    }

    /// <summary>
    /// Uploads the file to the senders drive then sends it to the recipients
    /// </summary>
    public async Task<(UploadResult uploadResult, string encryptedJsonContent64)> UploadAndTransferEncryptedFile(
        FileSystemType fileSystemType,
        UploadFileMetadata fileMetadata,
        StorageOptions storageOptions,
        TransitOptions transitOptions,
        string payloadData = "",
        ImageDataContent thumbnail = null)
    {
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        UploadInstructionSet instructionSet = new UploadInstructionSet()
        {
            TransferIv = transferIv,
            StorageOptions = storageOptions,
            TransitOptions = transitOptions
        };

        using (var client = CreateAppApiHttpClient(_token, fileSystemType))
        {
            var sharedSecret = _token.SharedSecret.ToSensitiveByteArray();
            var instructionStream = new MemoryStream(DotYouSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

            var encryptedJsonContent64 = keyHeader.EncryptDataAes(fileMetadata.AppData.JsonContent.ToUtf8ByteArray()).ToBase64();
            fileMetadata.AppData.JsonContent = encryptedJsonContent64;
            fileMetadata.PayloadIsEncrypted = true;

            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, instructionSet.TransferIv, ref sharedSecret),
                FileMetadata = fileMetadata
            };

            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, instructionSet.TransferIv, ref sharedSecret);

            //expect a payload if the caller says there should be one
            byte[] encryptedPayloadBytes = Array.Empty<byte>();
            if (fileMetadata.AppData.ContentIsComplete == false)
            {
                encryptedPayloadBytes = keyHeader.EncryptDataAes(payloadData.ToUtf8ByteArray());
            }

            List<StreamPart> parts = new()
            {
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                new StreamPart(new MemoryStream(encryptedPayloadBytes), "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload))
            };

            if (thumbnail != null)
            {
                var thumbnailCipherBytes = keyHeader.EncryptDataAesAsStream(thumbnail.Content);
                parts.Add(new StreamPart(thumbnailCipherBytes, thumbnail.GetFilename(), thumbnail.ContentType, Enum.GetName(MultipartUploadParts.Thumbnail)));
            }

            var driveSvc = CreateDriveService(client);
            ApiResponse<UploadResult> response = await driveSvc.Upload(parts.ToArray());

            Assert.That(response.IsSuccessStatusCode, Is.True);
            Assert.That(response.Content, Is.Not.Null);
            var uploadResult = response.Content;

            Assert.That(uploadResult.File, Is.Not.Null);
            Assert.That(uploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(uploadResult.File.TargetDrive, Is.Not.EqualTo(Guid.Empty));

            keyHeader.AesKey.Wipe();

            return (uploadResult, encryptedJsonContent64);
        }
    }

    //

    private IDriveTestHttpClientForApps CreateDriveService(HttpClient client)
    {
        return RefitCreator.RestServiceFor<IDriveTestHttpClientForApps>(client, _token.SharedSecret);
    }
}