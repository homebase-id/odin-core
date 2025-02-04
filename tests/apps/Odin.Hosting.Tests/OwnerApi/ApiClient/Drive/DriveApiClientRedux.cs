using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
using Odin.Hosting.Tests.AppAPI.Utils;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;

public class TestPayloadDefinition
{
    public byte[] Iv { get; set; } = Guid.Empty.ToByteArray();
    public string Key { get; set; }

    public string ContentType { get; set; }

    public byte[] Content { get; set; }

    public string DescriptorContent { get; set; }

    public ThumbnailContent PreviewThumbnail { get; set; }

    public List<ThumbnailContent> Thumbnails { get; set; }

    public UploadManifestPayloadDescriptor ToPayloadDescriptor(
        PayloadUpdateOperationType updateOperationType = PayloadUpdateOperationType.None)
    {
        var t = this.Thumbnails?.Select(thumb => new UploadedManifestThumbnailDescriptor()
        {
            ThumbnailKey = $"{this.Key}{thumb.PixelWidth}{thumb.PixelHeight}", //hulk smash (it all together)
            PixelWidth = thumb.PixelWidth,
            PixelHeight = thumb.PixelHeight
        });

        return new UploadManifestPayloadDescriptor
        {
            Iv = this.Iv,
            PayloadKey = this.Key,
            DescriptorContent = this.DescriptorContent,
            PreviewThumbnail = this.PreviewThumbnail,
            Thumbnails = t,
            PayloadUpdateOperationType = updateOperationType,
            ContentType = this.ContentType
        };
    }
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
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var storageOptions = new StorageOptions()
        {
            Drive = targetDrive,
        };

        var transitOptions = new TransitOptions()
        {
        };

        return await UploadNewMetadata(fileMetadata, storageOptions, transitOptions, fileSystemType);
    }

    /// <summary>
    /// Uploads a new file, unencrypted with metadata only; without any attachments (payload, thumbnails, etc.)
    /// </summary>
    public async Task<ApiResponse<UploadResult>> UploadNewMetadata(
        UploadFileMetadata fileMetadata,
        StorageOptions storageOptions,
        TransitOptions transitOptions,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        UploadInstructionSet instructionSet = new UploadInstructionSet()
        {
            TransferIv = transferIv,
            StorageOptions = storageOptions,
            TransitOptions = transitOptions
        };

        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
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

            var driveSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
            ApiResponse<UploadResult> response = await driveSvc.UploadStream(parts.ToArray());

            keyHeader.AesKey.Wipe();

            return response;
        }
    }


    public async Task<ApiResponse<UploadResult>> UpdateExistingMetadata(ExternalFileIdentifier file, Guid versionTag,
        UploadFileMetadata fileMetadata,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var transferIv = ByteArrayUtil.GetRndByteArray(16);

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

        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
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

            var driveSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
            ApiResponse<UploadResult> response = await driveSvc.UploadStream(parts.ToArray());

            return response;
        }
    }

    /// <summary>
    /// Uploads a new file, encrypted with metadata only; without any attachments (payload, thumbnails, etc.)
    /// </summary>
    public async Task<(ApiResponse<UploadResult> response, string encryptedJsonContent64)> UploadNewEncryptedMetadata(
        TargetDrive targetDrive,
        UploadFileMetadata fileMetadata,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var storageOptions = new StorageOptions()
        {
            Drive = targetDrive,
        };

        var transitOptions = new TransitOptions()
        {
        };

        return await UploadNewEncryptedMetadata(fileMetadata, storageOptions, transitOptions, fileSystemType);
    }

    public async Task<(ApiResponse<UploadResult> response, string encryptedJsonContent64)> UploadNewEncryptedMetadata(
        UploadFileMetadata fileMetadata,
        StorageOptions storageOptions,
        TransitOptions transitOptions,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        UploadInstructionSet instructionSet = new UploadInstructionSet()
        {
            TransferIv = transferIv,
            StorageOptions = storageOptions,
            TransitOptions = transitOptions
        };

        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
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
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json",
                    Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json",
                    Enum.GetName(MultipartUploadParts.Metadata)),
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
    public async Task<(ApiResponse<UploadResult> response,
            string encryptedJsonContent64,
            List<EncryptedAttachmentUploadResult> uploadedThumbnails,
            List<EncryptedAttachmentUploadResult> uploadedPayloads)>
        UploadNewEncryptedFile(TargetDrive targetDrive,
            UploadFileMetadata fileMetadata,
            UploadManifest uploadManifest,
            List<TestPayloadDefinition> payloads,
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
            },
            Manifest = uploadManifest
        };

        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
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
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json",
                    Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json",
                    Enum.GetName(MultipartUploadParts.Metadata)),
            };

            // Encrypt and add payloads
            foreach (var payloadDefinition in payloads)
            {
                var payloadCipher = keyHeader.EncryptDataAesAsStream(payloadDefinition.Content);
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
        UploadManifest uploadManifest,
        List<TestPayloadDefinition> payloads,
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
            },
            Manifest = uploadManifest
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
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json",
                    Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json",
                    Enum.GetName(MultipartUploadParts.Metadata)),
            };

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

            var driveSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
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

        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
        {
            var svc = RestService.For<IDriveTestHttpClientForOwner>(client);
            var response = await svc.UploadPayload(parts.ToArray());
            return response;
        }
    }

    public async Task<ApiResponse<DeletePayloadResult>> DeletePayload(ExternalFileIdentifier targetFile, Guid targetVersionTag, string payloadKey,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
        {
            var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, sharedSecret);
            var response = await svc.DeletePayload(new DeletePayloadRequest()
            {
                File = targetFile,
                VersionTag = targetVersionTag,
                Key = payloadKey
            });

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

    public async Task<ApiResponse<HttpContent>> GetThumbnail(ExternalFileIdentifier file, int width, int height, string payloadKey,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
        {
            var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, sharedSecret);

            var thumbnailResponse = await svc.GetThumbnailPost(new GetThumbnailRequest()
            {
                File = file,
                Height = height,
                Width = width,
                PayloadKey = payloadKey
            });

            return thumbnailResponse;
        }
    }

    public async Task<ApiResponse<DeleteFilesByGroupIdBatchResult>> DeleteFilesByGroupIdList(DeleteFilesByGroupIdBatchRequest batch,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
        {
            //wth - refit is not sending headers when you do GET request - why not!?
            var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, sharedSecret);
            var apiResponse = await svc.DeleteFilesByGroupIdBatch(batch);

            return apiResponse;
        }
    }

    public async Task<ApiResponse<DeleteFileIdBatchResult>> DeleteFileList(List<DeleteFileRequest> requests,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var batch = new DeleteFileIdBatchRequest()
        {
            Requests = requests
        };

        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
        {
            var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, sharedSecret);
            var apiResponse = await svc.DeleteFileIdBatch(batch);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<DeleteFileResult>> DeleteFile(ExternalFileIdentifier file, List<string> recipients = null,
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

    public async Task<ApiResponse<QueryBatchResponse>> QueryBatch(QueryBatchRequest request,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
        {
            var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, sharedSecret);

            var response = await svc.GetBatch(request);
            return response;
        }
    }

    public async Task<ApiResponse<QueryModifiedResult>> QueryModified(QueryModifiedRequest request,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
        {
            var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, sharedSecret);

            var response = await svc.GetModified(request);
            return response;
        }
    }

    public async Task<ApiResponse<QueryBatchCollectionResponse>> QueryBatchCollection(QueryBatchCollectionRequest request,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
        {
            var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, sharedSecret);

            var response = await svc.GetBatchCollection(request);
            return response;
        }
    }

    public async Task<RecipientTransferHistoryItem> WaitForTransferStatus(ExternalFileIdentifier file, OdinId recipient,
        LatestTransferStatus expectedStatus,
        FileSystemType fst = FileSystemType.Standard,
        TimeSpan? maxWaitTime = null)
    {
        var maxWait = maxWaitTime ?? TimeSpan.FromSeconds(10);

        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret, fst);
        var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, ownerSharedSecret);

        var sw = Stopwatch.StartNew();
        while (true)
        {
            var response = await svc.GetFileHeaderAsPost(file);
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception("Error occured while retrieving file to wait for transfer status");
            }

            var header = response.Content;
            if (header.ServerMetadata.TransferHistory.Recipients.TryGetValue(recipient, out var status)
                && status.LatestTransferStatus == expectedStatus)
            {
                return status;
            }

            if (sw.Elapsed > maxWait)
            {
                throw new TimeoutException($"timeout occured while waiting for a matching update " +
                                           $"to the transfer history.  latest status was {status?.LatestTransferStatus}");
            }

            await Task.Delay(100);
        }
    }
}