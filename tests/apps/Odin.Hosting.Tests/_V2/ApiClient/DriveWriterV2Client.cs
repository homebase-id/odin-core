using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public class DriveWriterV2Client(OdinId identity, IApiClientFactory factory, FileSystemType fileSystemType = FileSystemType.Standard)
{
    public SensitiveByteArray GetSharedSecret()
    {
        return factory.SharedSecret;
    }

    public async Task<ApiResponse<UploadResult>> UploadNewMetadata(Guid driveId, UploadFileMetadata fileMetadata)
    {
        var transitOptions = new TransitOptions();
        return await this.UploadNewMetadata(driveId, fileMetadata, transitOptions);
    }

    public async Task<ApiResponse<UploadResult>> UploadNewMetadata(Guid driveId,
        UploadFileMetadata fileMetadata,
        TransitOptions transitOptions)
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
            TransitOptions = transitOptions
        };

        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        {
            var instructionStream = new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

            fileMetadata.IsEncrypted = false;

            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, instructionSet.TransferIv, ref sharedSecret),
                FileMetadata = fileMetadata
            };

            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, instructionSet.TransferIv, ref sharedSecret);

            List<StreamPart> parts = new()
            {
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json",
                    Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json",
                    Enum.GetName(MultipartUploadParts.Metadata))
            };

            var driveSvc = RestService.For<IDriveWriterHttpClientApiV2>(client);
            ApiResponse<UploadResult> response = await driveSvc.UploadStream(parts.ToArray());

            keyHeader.AesKey.Wipe();

            return response;
        }
    }

    public async Task<(ApiResponse<UploadResult> response, string encryptedJsonContent64)> UpdateExistingEncryptedMetadata(
        ExternalFileIdentifier file,
        KeyHeader keyHeader,
        UploadFileMetadata fileMetadata)
    {
        var transferIv = ByteArrayUtil.GetRndByteArray(16);

        UploadInstructionSet instructionSet = new UploadInstructionSet()
        {
            TransferIv = transferIv,
            StorageOptions = new()
            {
                Drive = file.TargetDrive,
                OverwriteFileId = file.FileId,
                StorageIntent = StorageIntent.MetadataOnly
            },
            TransitOptions = new TransitOptions() { }
        };

        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        {
            var instructionStream = new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

            var encryptedJsonContent64 = keyHeader.EncryptDataAes(fileMetadata.AppData.Content.ToUtf8ByteArray()).ToBase64();
            fileMetadata.AppData.Content = encryptedJsonContent64;
            fileMetadata.IsEncrypted = true;

            var redactedKeyHeader = new KeyHeader()
            {
                Iv = keyHeader.Iv,
                AesKey = new SensitiveByteArray(Guid.Empty.ToByteArray())
            };

            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(redactedKeyHeader, instructionSet.TransferIv, ref sharedSecret),
                FileMetadata = fileMetadata
            };

            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, instructionSet.TransferIv, ref sharedSecret);
            List<StreamPart> parts =
            [
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json",
                    Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json",
                    Enum.GetName(MultipartUploadParts.Metadata))
            ];

            var driveSvc = RestService.For<IDriveWriterHttpClientApiV2>(client);
            ApiResponse<UploadResult> response = await driveSvc.UploadStream(driveId, parts.ToArray());

            return (response, encryptedJsonContent64);
        }
    }

    public async Task<ApiResponse<UploadResult>> UpdateExistingMetadata(ExternalFileIdentifier file, Guid versionTag,
        UploadFileMetadata fileMetadata)
    {
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        fileMetadata.VersionTag = versionTag;

        UploadInstructionSet instructionSet = new UploadInstructionSet()
        {
            TransferIv = transferIv,
            StorageOptions = new()
            {
                Drive = file.TargetDrive,
                OverwriteFileId = file.FileId,
                StorageIntent = StorageIntent.MetadataOnly
            },
            TransitOptions = new TransitOptions() { }
        };

        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        {
            var instructionStream = new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

            fileMetadata.IsEncrypted = false;

            var descriptor = new UploadFileDescriptor()
            {
                FileMetadata = fileMetadata
            };

            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, instructionSet.TransferIv, ref sharedSecret);

            List<StreamPart> parts = new()
            {
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json",
                    Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json",
                    Enum.GetName(MultipartUploadParts.Metadata))
            };

            var driveSvc = RestService.For<IDriveWriterHttpClientApiV2>(client);
            ApiResponse<UploadResult> response = await driveSvc.UploadStream(parts.ToArray());

            keyHeader.AesKey.Wipe();

            return response;
        }
    }

    /// <summary>
    /// Uploads a new file, encrypted with metadata only; without any attachments (payload, thumbnails, etc.)
    /// </summary>
    public async Task<(ApiResponse<UploadResult> response, string encryptedJsonContent64)> UploadNewEncryptedMetadata(
        UploadFileMetadata fileMetadata,
        StorageOptions storageOptions,
        TransitOptions transitOptions,
        KeyHeader keyHeader = null)
    {
        bool wipeKeyHeader = false;
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        if (keyHeader == null)
        {
            wipeKeyHeader = true;
            keyHeader = KeyHeader.NewRandom16();
        }

        UploadInstructionSet instructionSet = new UploadInstructionSet()
        {
            TransferIv = transferIv,
            StorageOptions = storageOptions,
            TransitOptions = transitOptions
        };

        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        {
            var instructionStream = new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

            var encryptedJsonContent64 = keyHeader.EncryptDataAes(fileMetadata.AppData.Content.ToUtf8ByteArray()).ToBase64();
            fileMetadata.AppData.Content = encryptedJsonContent64;
            fileMetadata.IsEncrypted = true;

            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, instructionSet.TransferIv, ref sharedSecret),
                FileMetadata = fileMetadata
            };

            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, instructionSet.TransferIv, ref sharedSecret);

            List<StreamPart> parts =
            [
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json",
                    Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json",
                    Enum.GetName(MultipartUploadParts.Metadata))
            ];

            var driveSvc = RestService.For<IDriveWriterHttpClientApiV2>(client);
            ApiResponse<UploadResult> response = await driveSvc.UploadStream(parts.ToArray());

            if (wipeKeyHeader)
            {
                keyHeader.AesKey.Wipe();
            }

            return (response, encryptedJsonContent64);
        }
    }

    public async Task<(ApiResponse<UploadResult> response, string encryptedJsonContent64)> UploadNewEncryptedMetadata(
        TargetDrive targetDrive,
        UploadFileMetadata fileMetadata,
        KeyHeader keyHeader = null)
    {
        keyHeader ??= KeyHeader.NewRandom16();

        var s = new StorageOptions()
        {
            Drive = targetDrive,
        };

        var t = new TransitOptions();

        return await UploadNewEncryptedMetadata(fileMetadata, s, t, keyHeader);
    }

    /// <summary>
    /// Uploads a new file, encrypted with metadata only; without any attachments (payload, thumbnails, etc.)
    /// </summary>
    public async Task<(ApiResponse<UploadResult> response, string encryptedJsonContent64, List<EncryptedAttachmentUploadResult>
        uploadedThumbnails, List<EncryptedAttachmentUploadResult> uploadedPayloads)> UploadNewEncryptedFile(TargetDrive targetDrive,
        KeyHeader keyHeader,
        UploadFileMetadata fileMetadata,
        UploadManifest uploadManifest,
        List<TestPayloadDefinition> payloads,
        AppNotificationOptions notificationOptions = null)
    {
        var uploadedThumbnails = new List<EncryptedAttachmentUploadResult>();
        var uploadedPayloads = new List<EncryptedAttachmentUploadResult>();

        var transferIv = ByteArrayUtil.GetRndByteArray(16);

        UploadInstructionSet instructionSet = new UploadInstructionSet()
        {
            TransferIv = transferIv,
            StorageOptions = new()
            {
                Drive = targetDrive,
            },
            TransitOptions = new TransitOptions
            {
                UseAppNotification = notificationOptions != null,
                AppNotificationOptions = notificationOptions
            },
            Manifest = uploadManifest,
        };

        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        {
            var instructionStream = new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

            var encryptedJsonContent64 = keyHeader.EncryptDataAes(fileMetadata.AppData.Content.ToUtf8ByteArray()).ToBase64();
            fileMetadata.AppData.Content = encryptedJsonContent64;
            fileMetadata.IsEncrypted = true;

            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, instructionSet.TransferIv, ref sharedSecret),
                FileMetadata = fileMetadata
            };

            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, instructionSet.TransferIv, ref sharedSecret);

            List<StreamPart> parts =
            [
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json",
                    Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json",
                    Enum.GetName(MultipartUploadParts.Metadata))
            ];

            // Encrypt and add payloads
            foreach (var payloadDefinition in payloads)
            {
                var payloadKeyHeader = new KeyHeader()
                {
                    Iv = payloadDefinition.Iv,
                    AesKey = new SensitiveByteArray(keyHeader.AesKey.GetKey())
                };

                var payloadCipher = payloadKeyHeader.EncryptDataAesAsStream(payloadDefinition.Content);
                parts.Add(new StreamPart(payloadCipher, payloadDefinition.Key, payloadDefinition.ContentType,
                    Enum.GetName(MultipartUploadParts.Payload)));
                uploadedPayloads.Add(new EncryptedAttachmentUploadResult()
                {
                    Key = payloadDefinition.Key,
                    ContentType = payloadDefinition.ContentType,
                    EncryptedContent64 = payloadCipher.ToByteArray().ToBase64()
                });

                payloadCipher.Position = 0;

                // Encrypt and add thumbnails
                foreach (var thumbnail in payloadDefinition.Thumbnails)
                {
                    var thumbnailCipher = payloadKeyHeader.EncryptDataAesAsStream(thumbnail.Content);
                    var thumbnailKey =
                        $"{payloadDefinition.Key}{thumbnail.PixelWidth}{thumbnail.PixelHeight}"; //hulk smash (it all together)
                    parts.Add(new StreamPart(thumbnailCipher, thumbnailKey, thumbnail.ContentType,
                        Enum.GetName(MultipartUploadParts.Thumbnail)));

                    uploadedThumbnails.Add(new EncryptedAttachmentUploadResult()
                    {
                        Key = thumbnailKey,
                        ContentType = thumbnail.ContentType,
                        EncryptedContent64 = thumbnailCipher.ToByteArray().ToBase64()
                    });
                    thumbnailCipher.Position = 0;
                }
            }

            var driveSvc = RestService.For<IDriveWriterHttpClientApiV2>(client);
            ApiResponse<UploadResult> response = await driveSvc.UploadStream(parts.ToArray());

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var code = WebScaffold.GetErrorCode(response.Error);
                throw new Exception($"BadRequest returned.  OdinClientErrorCode was {code}");
            }

            return (response, encryptedJsonContent64, uploadedThumbnails, uploadedPayloads);
        }
    }

    /// <summary>
    /// Uploads a new file, - unencrypted - with metadata only; without any attachments (payload, thumbnails, etc.)
    /// </summary>
    public async Task<ApiResponse<UploadResult>> UploadNewFile(
        TargetDrive targetDrive,
        UploadFileMetadata fileMetadata,
        UploadManifest uploadManifest,
        List<TestPayloadDefinition> payloads,
        TransitOptions transitOptions = null)
    {
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        if ((transitOptions?.Recipients?.Any() ?? false) && fileMetadata.AllowDistribution == false)
        {
            throw new Exception("You set recipients but did not allow file distribution; tsk tsk");
        }

        UploadInstructionSet instructionSet = new UploadInstructionSet()
        {
            TransferIv = transferIv,
            StorageOptions = new()
            {
                Drive = targetDrive,
            },
            TransitOptions = transitOptions ?? new TransitOptions()
            {
            },
            Manifest = uploadManifest
        };

        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        {
            var instructionStream = new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());
            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, instructionSet.TransferIv, ref sharedSecret),
                FileMetadata = fileMetadata
            };

            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, instructionSet.TransferIv, ref sharedSecret);

            List<StreamPart> parts =
            [
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json",
                    Enum.GetName(MultipartUploadParts.Instructions)),

                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json",
                    Enum.GetName(MultipartUploadParts.Metadata))
            ];

            foreach (var payloadDefinition in payloads)
            {
                parts.Add(new StreamPart(new MemoryStream(payloadDefinition.Content), payloadDefinition.Key, payloadDefinition.ContentType,
                    Enum.GetName(MultipartUploadParts.Payload)));

                foreach (var thumbnail in payloadDefinition.Thumbnails ?? new List<ThumbnailContent>())
                {
                    var thumbnailKey =
                        $"{payloadDefinition.Key}{thumbnail.PixelWidth}{thumbnail.PixelHeight}"; //hulk smash (it all together)
                    parts.Add(new StreamPart(new MemoryStream(thumbnail.Content), thumbnailKey, thumbnail.ContentType,
                        Enum.GetName(MultipartUploadParts.Thumbnail)));
                }
            }

            var driveSvc = RestService.For<IDriveWriterHttpClientApiV2>(client);
            ApiResponse<UploadResult> response = await driveSvc.UploadStream(parts.ToArray());

            keyHeader.AesKey.Wipe();

            return response;
        }
    }

    public async Task<ApiResponse<FileUpdateResult>> UpdateFile(
        FileUpdateInstructionSet uploadInstructionSet,
        UploadFileMetadata fileMetadata,
        List<TestPayloadDefinition> payloads)
    {
        var keyHeader = KeyHeader.NewRandom16();

        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        {
            var instructionStream = new MemoryStream(OdinSystemSerializer.Serialize(uploadInstructionSet).ToUtf8ByteArray());

            var descriptor = new UpdateFileDescriptor()
            {
                EncryptedKeyHeader = null,
                FileMetadata = fileMetadata
            };

            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, uploadInstructionSet.TransferIv, ref sharedSecret);

            List<StreamPart> parts =
            [
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json",
                    Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json",
                    Enum.GetName(MultipartUploadParts.Metadata))
            ];

            foreach (var payloadDefinition in payloads)
            {
                parts.Add(new StreamPart(new MemoryStream(payloadDefinition.Content), payloadDefinition.Key, payloadDefinition.ContentType,
                    Enum.GetName(MultipartUploadParts.Payload)));

                foreach (var thumbnail in payloadDefinition.Thumbnails ?? new List<ThumbnailContent>())
                {
                    var thumbnailKey =
                        $"{payloadDefinition.Key}{thumbnail.PixelWidth}{thumbnail.PixelHeight}"; //hulk smash (it all together)
                    parts.Add(new StreamPart(new MemoryStream(thumbnail.Content), thumbnailKey, thumbnail.ContentType,
                        Enum.GetName(MultipartUploadParts.Thumbnail)));
                }
            }

            var driveSvc = RestService.For<IDriveWriterHttpClientApiV2>(client);
            ApiResponse<FileUpdateResult> response = await driveSvc.UpdateFile(parts.ToArray());

            keyHeader.AesKey.Wipe();

            return response;
        }
    }


    public async Task<ApiResponse<UpdateLocalMetadataResult>> UpdateLocalAppMetadataTags(Guid driveId, Guid fileId,
        UpdateLocalMetadataTagsRequest request)
    {
        var keyHeader = KeyHeader.NewRandom16();

        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        {
            var driveSvc = RefitCreator.RestServiceFor<IDriveWriterHttpClientApiV2>(client, sharedSecret);
            ApiResponse<UpdateLocalMetadataResult> response = await driveSvc.UpdateLocalMetadataTags(driveId, fileId, request);

            keyHeader.AesKey.Wipe();

            return response;
        }
    }

    public async Task<ApiResponse<UpdateLocalMetadataResult>> UpdateLocalAppMetadataContent(Guid driveId, Guid fileId,
        UpdateLocalMetadataContentRequest request)
    {
        var keyHeader = KeyHeader.NewRandom16();

        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        {
            var driveSvc = RefitCreator.RestServiceFor<IDriveWriterHttpClientApiV2>(client, sharedSecret);
            ApiResponse<UpdateLocalMetadataResult> response = await driveSvc.UpdateLocalMetadataContent(driveId, fileId, request);

            keyHeader.AesKey.Wipe();

            return response;
        }
    }
    
    //
    
    public async Task<ApiResponse<DeleteFileResult>> SoftDeleteFile(ExternalFileIdentifier file, List<string> recipients = null)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        //wth - refit is not sending headers when you do GET request - why not!?
        var svc = RefitCreator.RestServiceFor<IUniversalDriveHttpClientApi>(client, sharedSecret);
        var apiResponse = await svc.SoftDeleteFile(new DeleteFileRequest()
        {
            File = file,
            Recipients = recipients
        });

        return apiResponse;
    }

    public async Task<ApiResponse<DeleteFilesByGroupIdBatchResult>> DeleteFilesByGroupIdList(DeleteFilesByGroupIdBatchRequest batch)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        //wth - refit is not sending headers when you do GET request - why not!?
        var svc = RefitCreator.RestServiceFor<IUniversalDriveHttpClientApi>(client, sharedSecret);
        var apiResponse = await svc.DeleteFilesByGroupIdBatch(batch);

        return apiResponse;
    }

    public async Task<ApiResponse<DeleteFileIdBatchResult>> DeleteFileList(List<DeleteFileRequest> requests)
    {
        var batch = new DeleteFileIdBatchRequest()
        {
            Requests = requests
        };

        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        var svc = RefitCreator.RestServiceFor<IUniversalDriveHttpClientApi>(client, sharedSecret);
        var apiResponse = await svc.DeleteFileIdBatch(batch);
        return apiResponse;
    }
}