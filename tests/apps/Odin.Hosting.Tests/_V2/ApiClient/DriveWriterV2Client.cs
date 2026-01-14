using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.UnifiedV2.Drive;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public class DriveWriterV2Client(OdinId identity, IApiClientFactory factory, FileSystemType fileSystemType = FileSystemType.Standard)
{
    public SensitiveByteArray GetSharedSecret()
    {
        return factory.SharedSecret;
    }

    public async Task<ApiResponse<CreateFileResult>> UploadNewMetadata(
        Guid driveId,
        UploadFileMetadata fileMetadata)
    {
        return await UploadNewMetadata(
            driveId,
            fileMetadata,
            new TransitOptions());
    }

    public async Task<ApiResponse<CreateFileResult>> UploadNewMetadata(
        Guid driveId,
        UploadFileMetadata fileMetadata,
        TransitOptions transitOptions)
    {
        // Explicitly mark as unencrypted metadata
        fileMetadata.IsEncrypted = false;

        return await CreateNewUnencryptedFile(
            driveId: driveId,
            fileMetadata: fileMetadata,
            uploadManifest: null,
            payloads: [],
            transitOptions: transitOptions
        );
    }

    public async Task<(
        ApiResponse<CreateFileResult> response,
        string encryptedJsonContent64,
        List<EncryptedAttachmentUploadResult> uploadedThumbnails,
        List<EncryptedAttachmentUploadResult> uploadedPayloads
        )> CreateEncryptedFile(
        UploadFileMetadata fileMetadata,
        StorageOptions storageOptions,
        TransitOptions transitOptions,
        UploadManifest uploadManifest = null,
        List<TestPayloadDefinition> payloads = null,
        AppNotificationOptions notificationOptions = null,
        KeyHeader keyHeader = null)
    {
        if ((transitOptions?.Recipients?.Any() ?? false) && fileMetadata.AllowDistribution == false)
        {
            throw new Exception("You set recipients but did not allow file distribution; tsk tsk");
        }

        var uploadedThumbnails = new List<EncryptedAttachmentUploadResult>();
        var uploadedPayloads = new List<EncryptedAttachmentUploadResult>();

        var wipeKeyHeader = false;
        if (keyHeader == null)
        {
            keyHeader = KeyHeader.NewRandom16();
            wipeKeyHeader = true;
        }

        var transferIv = ByteArrayUtil.GetRndByteArray(16);

        var instructionSet = new UploadInstructionSet
        {
            TransferIv = transferIv,
            StorageOptions = storageOptions,
            TransitOptions = transitOptions ?? new TransitOptions(),
            Manifest = uploadManifest
        };

        if (notificationOptions != null)
        {
            instructionSet.TransitOptions.UseAppNotification = true;
            instructionSet.TransitOptions.AppNotificationOptions = notificationOptions;
        }

        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);

        var instructionStream = new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

        // Encrypt metadata content
        var encryptedJsonContent64 = keyHeader.EncryptDataAes(fileMetadata.AppData.Content.ToUtf8ByteArray()).ToBase64();

        fileMetadata.AppData.Content = encryptedJsonContent64;
        fileMetadata.IsEncrypted = true;

        var descriptor = new UploadFileDescriptor
        {
            EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(
                keyHeader,
                instructionSet.TransferIv,
                ref sharedSecret),
            FileMetadata = fileMetadata
        };

        var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, instructionSet.TransferIv, ref sharedSecret);

        List<StreamPart> parts =
        [
            new StreamPart(
                instructionStream,
                "instructionSet.encrypted",
                "application/json",
                Enum.GetName(MultipartUploadParts.Instructions)),

            new StreamPart(
                fileDescriptorCipher,
                "fileDescriptor.encrypted",
                "application/json",
                Enum.GetName(MultipartUploadParts.Metadata))
        ];

        // Optional encrypted payloads + thumbnails
        if (payloads != null && payloads.Count > 0)
        {
            foreach (var payload in payloads)
            {
                var payloadKeyHeader = new KeyHeader
                {
                    Iv = payload.Iv,
                    AesKey = new SensitiveByteArray(keyHeader.AesKey.GetKey())
                };

                var payloadCipher = payloadKeyHeader.EncryptDataAesAsStream(payload.Content);

                parts.Add(new StreamPart(
                    payloadCipher,
                    payload.Key,
                    payload.ContentType,
                    Enum.GetName(MultipartUploadParts.Payload)));

                uploadedPayloads.Add(new EncryptedAttachmentUploadResult
                {
                    Key = payload.Key,
                    ContentType = payload.ContentType,
                    EncryptedContent64 = payloadCipher.ToByteArray().ToBase64()
                });

                payloadCipher.Position = 0;

                foreach (var thumb in payload.Thumbnails)
                {
                    var thumbCipher = payloadKeyHeader.EncryptDataAesAsStream(thumb.Content);

                    var thumbKey = $"{payload.Key}{thumb.PixelWidth}{thumb.PixelHeight}";

                    parts.Add(new StreamPart(
                        thumbCipher,
                        thumbKey,
                        thumb.ContentType,
                        Enum.GetName(MultipartUploadParts.Thumbnail)));

                    uploadedThumbnails.Add(new EncryptedAttachmentUploadResult
                    {
                        Key = thumbKey,
                        ContentType = thumb.ContentType,
                        EncryptedContent64 = thumbCipher.ToByteArray().ToBase64()
                    });

                    thumbCipher.Position = 0;
                }
            }
        }

        var driveId = storageOptions.DriveId;
        var driveSvc = RestService.For<IDriveWriterHttpClientApiV2>(client);
        var response = await driveSvc.CreateNewFile(driveId, parts.ToArray());

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var code = WebScaffold.GetErrorCode(response.Error);
            throw new Exception($"BadRequest returned. OdinClientErrorCode was {code}");
        }

        if (wipeKeyHeader)
            keyHeader.AesKey.Wipe();

        return (response, encryptedJsonContent64, uploadedThumbnails, uploadedPayloads);
    }

    public async Task<ApiResponse<CreateFileResult>> CreateNewUnencryptedFile(
        Guid driveId,
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
                DriveId = driveId,
            },
            TransitOptions = transitOptions ?? new TransitOptions(),
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
            ApiResponse<CreateFileResult> response = await driveSvc.CreateNewFile(driveId, parts.ToArray());

            keyHeader.AesKey.Wipe();

            return response;
        }
    }

    public async Task<ApiResponse<UpdateFileResult>> UpdateFile(
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

            var driveId = uploadInstructionSet.File.DriveId;
            var fileId = uploadInstructionSet.File.FileId.GetValueOrDefault();
            var driveSvc = RestService.For<IDriveWriterHttpClientApiV2>(client);
            ApiResponse<UpdateFileResult> response = await driveSvc.UpdateFileByFileId(driveId, fileId, parts.ToArray());

            keyHeader.AesKey.Wipe();

            return response;
        }
    }


    public async Task<ApiResponse<UpdateLocalMetadataResult>> UpdateLocalAppMetadataTags(Guid driveId, Guid fileId,
        UpdateLocalMetadataTagsRequestV2 request)
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
        UpdateLocalMetadataContentRequestV2 request)
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

    public async Task<ApiResponse<DeleteFileResultV2>> SoftDeleteFile(Guid driveId, Guid fileId, List<string> recipients = null)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        var svc = RefitCreator.RestServiceFor<IDriveWriterHttpClientApiV2>(client, sharedSecret);
        var apiResponse = await svc.SoftDeleteFile(driveId, fileId, new DeleteFileOptionsV2()
        {
            Recipients = recipients
        });

        return apiResponse;
    }

    public async Task<ApiResponse<DeleteFilesByGroupIdBatchResultV2>> DeleteFilesByGroupIdList(Guid driveId,
        DeleteFilesByGroupIdBatchRequestV2 batch)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        var svc = RefitCreator.RestServiceFor<IDriveWriterHttpClientApiV2>(client, sharedSecret);
        var apiResponse = await svc.DeleteFilesByGroupIdBatch(driveId, batch);

        return apiResponse;
    }

    public async Task<ApiResponse<DeleteFileIdBatchResultV2>> DeleteFileList(Guid driveId, List<DeleteFileRequestV2> requests)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        var svc = RefitCreator.RestServiceFor<IDriveWriterHttpClientApiV2>(client, sharedSecret);
        var apiResponse = await svc.DeleteFileIdBatch(driveId, new DeleteFileIdBatchRequestV2
        {
            Requests = requests
        });
        return apiResponse;
    }

    public async Task<ApiResponse<DeletePayloadResult>> DeletePayload(Guid driveId, Guid fileId, string key, Guid versionTag)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        var svc = RefitCreator.RestServiceFor<IDriveWriterHttpClientApiV2>(client, sharedSecret);
        var apiResponse = await svc.DeletePayload(driveId, fileId, new DeletePayloadRequestV2
            {
                Key = key,
                VersionTag = versionTag
            }
        );

        return apiResponse;
    }

    public async Task<ApiResponse<SendReadReceiptResultV2>> SendReadReceipt(Guid driveId, List<Guid> files)
    {
        var request = new SendReadReceiptRequestV2
        {
            Files = files
        };

        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        var svc = RefitCreator.RestServiceFor<IDriveWriterHttpClientApiV2>(client, sharedSecret);
        var apiResponse = await svc.SendReadReceiptBatch(driveId, request);
        return apiResponse;
    }
}