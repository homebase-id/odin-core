using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Core.Serialization;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem.Base;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.Encryption;
using Odin.Core.Services.Peer.SendingHost;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Tests.AppAPI.Utils;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;

public class TestPayloadDefinition
{
    public string Key { get; set; }
    public string ContentType { get; set; }
    public byte[] Content { get; set; }
}

public class EncryptedAttachmentUploadResult
{
    public string Key { get; init; }
    public string ContentType { get; init; }
    public string EncryptedContent64 { get; init; }
}

public class DriveApiClientRedux
{
    private readonly TestIdentity _identity;
    private readonly OwnerApiTestUtils _ownerApi;

    public DriveApiClientRedux(OwnerApiTestUtils ownerApi, TestIdentity identity)
    {
        _ownerApi = ownerApi;
        _identity = identity;
    }

    /// <summary>
    /// Uploads a new file, unencrypted with metadata only; without any attachments (payload, thumbnails, etc.)
    /// </summary>
    public async Task<ApiResponse<UploadResult>> UploadNewMetadata(TargetDrive targetDrive,
        UploadFileMetadata fileMetadata,
        bool useGlobalTransitId = false,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        UploadInstructionSet instructionSet = new UploadInstructionSet()
        {
            TransferIv = transferIv,
            StorageOptions = new()
            {
                Drive = targetDrive,
                OverwriteFileId = default
            },
            TransitOptions = new TransitOptions()
            {
                UseGlobalTransitId = useGlobalTransitId
            }
        };

        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
        {
            var instructionStream = new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

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
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata))
            };

            var driveSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
            ApiResponse<UploadResult> response = await driveSvc.UploadStream(parts.ToArray());

            keyHeader.AesKey.Wipe();

            return response;
        }
    }

    /// <summary>
    /// Uploads a new file, encrypted with metadata only; without any attachments (payload, thumbnails, etc.)
    /// </summary>
    public async Task<(ApiResponse<UploadResult> response, string encryptedJsonContent64)> UploadNewEncryptedMetadata(TargetDrive targetDrive,
        UploadFileMetadata fileMetadata,
        bool useGlobalTransitId = false,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        UploadInstructionSet instructionSet = new UploadInstructionSet()
        {
            TransferIv = transferIv,
            StorageOptions = new()
            {
                Drive = targetDrive,
            },
            TransitOptions = new TransitOptions()
            {
                UseGlobalTransitId = useGlobalTransitId
            }
        };

        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
        {
            var instructionStream = new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

            var encryptedJsonContent64 = keyHeader.EncryptDataAes(fileMetadata.AppData.JsonContent.ToUtf8ByteArray()).ToBase64();
            fileMetadata.AppData.JsonContent = encryptedJsonContent64;
            fileMetadata.PayloadIsEncrypted = true;

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
            };

            var driveSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
            ApiResponse<UploadResult> response = await driveSvc.UploadStream(parts.ToArray());

            keyHeader.AesKey.Wipe();

            return (response, encryptedJsonContent64);
        }
    }

    /// <summary>
    /// Uploads a new file, encrypted with metadata only; without any attachments (payload, thumbnails, etc.)
    /// </summary>
    public async Task<(ApiResponse<UploadResult> response, string encryptedJsonContent64, List<EncryptedAttachmentUploadResult> uploadedThumbnails,
            List<EncryptedAttachmentUploadResult> uploadedPayloads)>
        UploadNewEncryptedFile(TargetDrive targetDrive,
            UploadFileMetadata fileMetadata,
            List<ImageDataContent> thumbnails,
            List<TestPayloadDefinition> payloads,
            bool useGlobalTransitId = false,
            FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var uploadedThumbnails = new List<EncryptedAttachmentUploadResult>();
        var uploadedPayloads = new List<EncryptedAttachmentUploadResult>();

        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        UploadInstructionSet instructionSet = new UploadInstructionSet()
        {
            TransferIv = transferIv,
            StorageOptions = new()
            {
                Drive = targetDrive,
            },
            TransitOptions = new TransitOptions()
            {
                UseGlobalTransitId = useGlobalTransitId
            }
        };

        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
        {
            var instructionStream = new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

            var encryptedJsonContent64 = keyHeader.EncryptDataAes(fileMetadata.AppData.JsonContent.ToUtf8ByteArray()).ToBase64();
            fileMetadata.AppData.JsonContent = encryptedJsonContent64;
            fileMetadata.PayloadIsEncrypted = true;

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
            };

            // Encrypt and add payloads
            foreach (var payloadDefinition in payloads)
            {
                var payloadCipher = keyHeader.EncryptDataAesAsStream(payloadDefinition.ContentType.ToUtf8ByteArray());
                parts.Add(new StreamPart(payloadCipher, payloadDefinition.Key, payloadDefinition.ContentType, Enum.GetName(MultipartUploadParts.Payload)));

                uploadedPayloads.Add(new EncryptedAttachmentUploadResult()
                {
                    Key = payloadDefinition.Key,
                    ContentType = payloadDefinition.ContentType,
                    EncryptedContent64 = payloadCipher.ToByteArray().ToBase64()
                });
            }

            // Encrypt and add thumbnails
            foreach (var thumbnail in thumbnails)
            {
                var thumbnailCipher = keyHeader.EncryptDataAesAsStream(thumbnail.Content);
                parts.Add(new StreamPart(thumbnailCipher, thumbnail.GetFilename(), thumbnail.ContentType,
                    Enum.GetName(MultipartUploadParts.Thumbnail)));

                uploadedThumbnails.Add(new EncryptedAttachmentUploadResult()
                {
                    Key = thumbnail.GetFilename(),
                    ContentType = thumbnail.ContentType,
                    EncryptedContent64 = thumbnailCipher.ToByteArray().ToBase64()
                });
            }

            var driveSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
            ApiResponse<UploadResult> response = await driveSvc.UploadStream(parts.ToArray());

            keyHeader.AesKey.Wipe();

            return (response, encryptedJsonContent64, uploadedThumbnails, uploadedPayloads);
        }
    }

    /// <summary>
    /// Uploads a new file, - unencrypted - with metadata only; without any attachments (payload, thumbnails, etc.)
    /// </summary>
    public async Task<ApiResponse<UploadResult>> UploadNewFile(
        TargetDrive targetDrive,
        UploadFileMetadata fileMetadata,
        List<ImageDataContent> thumbnails,
        List<TestPayloadDefinition> payloads,
        bool useGlobalTransitId = false,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        UploadInstructionSet instructionSet = new UploadInstructionSet()
        {
            TransferIv = transferIv,
            StorageOptions = new()
            {
                Drive = targetDrive,
            },
            TransitOptions = new TransitOptions()
            {
                UseGlobalTransitId = useGlobalTransitId
            }
        };

        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
        {
            var instructionStream = new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());
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
            };

            foreach (var payloadDefinition in payloads)
            {
                parts.Add(new StreamPart(new MemoryStream(payloadDefinition.Content), payloadDefinition.Key, payloadDefinition.ContentType,
                    Enum.GetName(MultipartUploadParts.Payload)));
            }

            // Encrypt and add thumbnails
            foreach (var thumbnail in thumbnails)
            {
                parts.Add(new StreamPart(new MemoryStream(thumbnail.Content), thumbnail.GetFilename(), thumbnail.ContentType,
                    Enum.GetName(MultipartUploadParts.Thumbnail)));
            }

            var driveSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
            ApiResponse<UploadResult> response = await driveSvc.UploadStream(parts.ToArray());

            keyHeader.AesKey.Wipe();

            return response;
        }
    }

    public async Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeader(ExternalFileIdentifier file,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
        {
            //wth - refit is not sending headers when you do GET request - why not!?
            var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, sharedSecret);
            // var apiResponse = await svc.GetFileHeader(file.FileId, file.TargetDrive.Alias, file.TargetDrive.Type);
            var apiResponse = await svc.GetFileHeaderAsPost(file);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> GetPayload(ExternalFileIdentifier file, string key, FileChunk chunk = null,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
        {
            //wth - refit is not sending headers when you do GET request - why not!?
            var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, sharedSecret);
            return await svc.GetPayloadPost(new GetPayloadRequest()
            {
                File = file,
                Chunk = chunk,
                Key = key
            });
        }
    }

    public async Task<ApiResponse<HttpContent>> GetThumbnail(ExternalFileIdentifier file, int width, int height,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
        {
            var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, sharedSecret);

            var thumbnailResponse = await svc.GetThumbnailPost(new GetThumbnailRequest()
            {
                File = file,
                Height = height,
                Width = width
            });

            return thumbnailResponse;
        }
    }

    public async Task<ApiResponse<DeleteLinkedFileResult>> DeleteFile(ExternalFileIdentifier file, List<string> recipients = null,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
        {
            //wth - refit is not sending headers when you do GET request - why not!?
            var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, sharedSecret);
            var apiResponse = await svc.DeleteFile(new DeleteFileRequest()
            {
                File = file,
                Recipients = recipients
            });

            return apiResponse;
        }
    }
}