using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Services.Apps;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.FileSystem.Base.Upload.Attachments;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.Base.Drive.Status;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests.AppAPI.Utils;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Org.BouncyCastle.Asn1.X509;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Drive;

public class UniversalDriveApiClient(OdinId identity, IApiClientFactory factory)
{
    /// <summary>
    /// Uploads a new file, unencrypted with metadata only; without any attachments (payload, thumbnails, etc.)
    /// </summary>
    public async Task<ApiResponse<UploadResult>> UploadNewMetadata(TargetDrive targetDrive,
        UploadFileMetadata fileMetadata,
        bool useGlobalTransitId = false,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var transitOptions = new TransitOptions()
        {
            UseGlobalTransitId = useGlobalTransitId
        };

        return await this.UploadNewMetadata(targetDrive, fileMetadata, transitOptions, fileSystemType);
    }

    public async Task<ApiResponse<UploadResult>> UploadNewMetadata(TargetDrive targetDrive,
        UploadFileMetadata fileMetadata,
        TransitOptions transitOptions,
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
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata))
            };

            var driveSvc = RestService.For<IUniversalDriveHttpClientApi>(client);
            ApiResponse<UploadResult> response = await driveSvc.UploadStream(parts.ToArray());

            keyHeader.AesKey.Wipe();

            return response;
        }
    }

    public async Task<ApiResponse<UploadResult>> UpdateExistingMetadata(ExternalFileIdentifier file, Guid versionTag, UploadFileMetadata fileMetadata,
        FileSystemType fileSystemType = FileSystemType.Standard)
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
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata))
            };

            var driveSvc = RestService.For<IUniversalDriveHttpClientApi>(client);
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
        KeyHeader keyHeader = null,
        FileSystemType fileSystemType = FileSystemType.Standard)
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

            List<StreamPart> parts = new()
            {
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
            };

            var driveSvc = RestService.For<IUniversalDriveHttpClientApi>(client);
            ApiResponse<UploadResult> response = await driveSvc.UploadStream(parts.ToArray());

            if (wipeKeyHeader)
            {
                keyHeader.AesKey.Wipe();
            }

            return (response, encryptedJsonContent64);
        }
    }

    public async Task<(ApiResponse<UploadResult> response, string encryptedJsonContent64)> UploadNewEncryptedMetadata(TargetDrive targetDrive,
        UploadFileMetadata fileMetadata,
        bool useGlobalTransitId = false,
        KeyHeader keyHeader = null,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        keyHeader ??= KeyHeader.NewRandom16();

        var s = new StorageOptions()
        {
            Drive = targetDrive,
        };

        var t = new TransitOptions()
        {
            UseGlobalTransitId = useGlobalTransitId
        };

        return await UploadNewEncryptedMetadata(fileMetadata, s, t, keyHeader, fileSystemType);
    }

    /// <summary>
    /// Uploads a new file, encrypted with metadata only; without any attachments (payload, thumbnails, etc.)
    /// </summary>
    public async Task<(ApiResponse<UploadResult> response, string encryptedJsonContent64, List<EncryptedAttachmentUploadResult> uploadedThumbnails,
            List<EncryptedAttachmentUploadResult> uploadedPayloads)>
        UploadNewEncryptedFile(TargetDrive targetDrive,
            UploadFileMetadata fileMetadata,
            UploadManifest uploadManifest,
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
            },
            Manifest = uploadManifest
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

            List<StreamPart> parts = new()
            {
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
            };

            // Encrypt and add payloads
            foreach (var payloadDefinition in payloads)
            {
                var payloadCipher = keyHeader.EncryptDataAesAsStream(payloadDefinition.Content);
                parts.Add(new StreamPart(payloadCipher, payloadDefinition.Key, payloadDefinition.ContentType, Enum.GetName(MultipartUploadParts.Payload)));
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
            }

            var driveSvc = RestService.For<IUniversalDriveHttpClientApi>(client);
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
        UploadManifest uploadManifest,
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

            List<StreamPart> parts = new()
            {
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
            };

            foreach (var payloadDefinition in payloads)
            {
                parts.Add(new StreamPart(new MemoryStream(payloadDefinition.Content), payloadDefinition.Key, payloadDefinition.ContentType,
                    Enum.GetName(MultipartUploadParts.Payload)));

                foreach (var thumbnail in payloadDefinition.Thumbnails ?? new List<ThumbnailContent>())
                {
                    var thumbnailKey = $"{payloadDefinition.Key}{thumbnail.PixelWidth}{thumbnail.PixelHeight}"; //hulk smash (it all together)
                    parts.Add(new StreamPart(new MemoryStream(thumbnail.Content), thumbnailKey, thumbnail.ContentType,
                        Enum.GetName(MultipartUploadParts.Thumbnail)));
                }
            }

            var driveSvc = RestService.For<IUniversalDriveHttpClientApi>(client);
            ApiResponse<UploadResult> response = await driveSvc.UploadStream(parts.ToArray());

            keyHeader.AesKey.Wipe();

            return response;
        }
    }

    public async Task<ApiResponse<UploadPayloadResult>> UploadPayloads(
        ExternalFileIdentifier targetFile,
        Guid targetVersionTag,
        UploadManifest uploadManifest,
        List<TestPayloadDefinition> payloads,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var instructionSet = new UploadPayloadInstructionSet()
        {
            TargetFile = targetFile,
            Manifest = uploadManifest,
            VersionTag = targetVersionTag,
            Recipients = default
        };

        var instructionSetBytes = OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray();

        List<StreamPart> parts = new();
        parts.Add(new StreamPart(new MemoryStream(instructionSetBytes), "instructionSet", "application/json",
            Enum.GetName(MultipartUploadParts.PayloadUploadInstructions)));

        foreach (var payloadDefinition in payloads)
        {
            parts.Add(new StreamPart(new MemoryStream(payloadDefinition.Content), payloadDefinition.Key, payloadDefinition.ContentType,
                Enum.GetName(MultipartUploadParts.Payload)));

            foreach (var thumbnail in payloadDefinition.Thumbnails)
            {
                var thumbnailKey = $"{payloadDefinition.Key}{thumbnail.PixelWidth}{thumbnail.PixelHeight}"; //hulk smash (it all together)
                parts.Add(new StreamPart(new MemoryStream(thumbnail.Content), thumbnailKey, thumbnail.ContentType,
                    Enum.GetName(MultipartUploadParts.Thumbnail)));
            }
        }

        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        {
            var svc = RestService.For<IUniversalDriveHttpClientApi>(client);
            var response = await svc.UploadPayload(parts.ToArray());
            return response;
        }
    }

    public async Task<(ApiResponse<UploadPayloadResult> response, Dictionary<string, byte[]> encryptedPayloads64)> UploadEncryptedPayloads(
        ExternalFileIdentifier targetFile,
        Guid targetVersionTag,
        UploadManifest uploadManifest,
        List<TestPayloadDefinition> payloads,
        byte[] aesKey,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var instructionSet = new UploadPayloadInstructionSet()
        {
            TargetFile = targetFile,
            Manifest = uploadManifest,
            VersionTag = targetVersionTag,
            Recipients = default
        };

        var instructionSetBytes = OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray();

        List<StreamPart> parts = new();
        parts.Add(new StreamPart(new MemoryStream(instructionSetBytes), "instructionSet", "application/json",
            Enum.GetName(MultipartUploadParts.PayloadUploadInstructions)));

        var encryptedPayloads64 = new Dictionary<string, byte[]>();
        foreach (var payloadDefinition in payloads)
        {
            var payloadKeyHeader = new KeyHeader()
            {
                Iv = payloadDefinition.Iv,
                AesKey = aesKey.ToSensitiveByteArray()
            };

            var encryptedPayloadStream = payloadKeyHeader.EncryptDataAesAsStream(payloadDefinition.Content);
            var encryptedPayloadStream2 = payloadKeyHeader.EncryptDataAesAsStream(payloadDefinition.Content);
            encryptedPayloads64.Add(payloadDefinition.Key, encryptedPayloadStream2.ToByteArray());

            parts.Add(new StreamPart(encryptedPayloadStream, payloadDefinition.Key, payloadDefinition.ContentType,
                Enum.GetName(MultipartUploadParts.Payload)));

            foreach (var thumbnail in payloadDefinition.Thumbnails)
            {
                var encryptedThumbnailStream = payloadKeyHeader.EncryptDataAesAsStream(thumbnail.Content);
                var thumbnailKey = $"{payloadDefinition.Key}{thumbnail.PixelWidth}{thumbnail.PixelHeight}"; //hulk smash (it all together)
                parts.Add(new StreamPart(encryptedThumbnailStream, thumbnailKey, thumbnail.ContentType,
                    Enum.GetName(MultipartUploadParts.Thumbnail)));
            }
        }

        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        {
            var svc = RestService.For<IUniversalDriveHttpClientApi>(client);
            var response = await svc.UploadPayload(parts.ToArray());
            return (response, encryptedPayloads64);
        }
    }

    public async Task<ApiResponse<DeletePayloadResult>> DeletePayload(ExternalFileIdentifier targetFile, Guid targetVersionTag, string payloadKey,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        var svc = RefitCreator.RestServiceFor<IUniversalDriveHttpClientApi>(client, sharedSecret);
        var response = await svc.DeletePayload(new DeletePayloadRequest()
        {
            File = targetFile,
            VersionTag = targetVersionTag,
            Key = payloadKey
        });

        return response;
    }

    public async Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeader(ExternalFileIdentifier file,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        //wth - refit is not sending headers when you do GET request - why not!?
        var svc = RefitCreator.RestServiceFor<IUniversalDriveHttpClientApi>(client, sharedSecret);
        // var apiResponse = await svc.GetFileHeader(file.FileId, file.TargetDrive.Alias, file.TargetDrive.Type);
        var apiResponse = await svc.GetFileHeaderAsPost(file);
        return apiResponse;
    }

    public async Task<ApiResponse<HttpContent>> GetPayload(ExternalFileIdentifier file, string key, FileChunk chunk = null,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        //wth - refit is not sending headers when you do GET request - why not!?
        var svc = RefitCreator.RestServiceFor<IUniversalDriveHttpClientApi>(client, sharedSecret);
        return await svc.GetPayloadPost(new GetPayloadRequest()
        {
            File = file,
            Chunk = chunk,
            Key = key
        });
    }

    public async Task<ApiResponse<HttpContent>> GetThumbnail(ExternalFileIdentifier file, int width, int height, string payloadKey,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        var svc = RefitCreator.RestServiceFor<IUniversalDriveHttpClientApi>(client, sharedSecret);

        var thumbnailResponse = await svc.GetThumbnailPost(new GetThumbnailRequest()
        {
            File = file,
            Height = height,
            Width = width,
            PayloadKey = payloadKey
        });

        return thumbnailResponse;
    }

    public async Task<ApiResponse<DeleteFileResult>> DeleteFile(ExternalFileIdentifier file, List<string> recipients = null,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        //wth - refit is not sending headers when you do GET request - why not!?
        var svc = RefitCreator.RestServiceFor<IUniversalDriveHttpClientApi>(client, sharedSecret);
        var apiResponse = await svc.DeleteFile(new DeleteFileRequest()
        {
            File = file,
            Recipients = recipients
        });

        return apiResponse;
    }

    public async Task<ApiResponse<DeleteFilesByGroupIdBatchResult>> DeleteFilesByGroupIdList(DeleteFilesByGroupIdBatchRequest batch,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        //wth - refit is not sending headers when you do GET request - why not!?
        var svc = RefitCreator.RestServiceFor<IUniversalDriveHttpClientApi>(client, sharedSecret);
        var apiResponse = await svc.DeleteFilesByGroupIdBatch(batch);

        return apiResponse;
    }

    public async Task<ApiResponse<DeleteFileIdBatchResult>> DeleteFileList(List<DeleteFileRequest> requests,
        FileSystemType fileSystemType = FileSystemType.Standard)
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

    public async Task<ApiResponse<QueryBatchResponse>> QueryBatch(QueryBatchRequest request, FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        var svc = RefitCreator.RestServiceFor<IUniversalDriveHttpClientApi>(client, sharedSecret);
        var response = await svc.GetBatch(request);
        return response;
    }

    public async Task<ApiResponse<QueryModifiedResult>> QueryModified(QueryModifiedRequest request, FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        var svc = RefitCreator.RestServiceFor<IUniversalDriveHttpClientApi>(client, sharedSecret);
        var response = await svc.GetModified(request);
        return response;
    }

    public async Task<ApiResponse<QueryBatchCollectionResponse>> QueryBatchCollection(QueryBatchCollectionRequest request,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        var svc = RefitCreator.RestServiceFor<IUniversalDriveHttpClientApi>(client, sharedSecret);
        var response = await svc.GetBatchCollection(request);
        return response;
    }

    public async Task<ApiResponse<InboxStatus>> ProcessInbox(TargetDrive drive, int batchSize = 1)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IUniversalDriveHttpClientApi>(client, sharedSecret);
        var response = await svc.ProcessInbox(new ProcessInboxRequest()
        {
            TargetDrive = drive,
            BatchSize = batchSize
        });

        return response;
    }

    public async Task WaitForEmptyOutbox(TargetDrive drive, TimeSpan? maxWaitTime = null)
    {
        var maxWait = maxWaitTime ?? TimeSpan.FromSeconds(10);
        
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IUniversalDriveHttpClientApi>(client, sharedSecret);

        var sw = Stopwatch.StartNew();
        while (true)
        {
            var response = await svc.GetDriveStatus(drive.Alias, drive.Type);
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception("Error occured while retrieving outbox status");
            }

            var status = response.Content;
            if (status.Outbox.TotalItems == 0)
            {
                return;
            }

            if (sw.Elapsed > maxWait)
            {
                throw new TimeoutException($"timeout occured while waiting for outbox to complete processing");
            }

            await Task.Delay(100);
        }
    }

    public async Task<ApiResponse<DriveStatus>> GetDriveStatus(TargetDrive drive)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var driveSvc = RefitCreator.RestServiceFor<IUniversalDriveHttpClientApi>(client, sharedSecret);
        var response = await driveSvc.GetDriveStatus(drive.Alias, drive.Type);
        return response;
    }
}