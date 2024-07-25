using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.FileSystem.Base.Upload.Attachments;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Peer.Direct;

public class UniversalPeerDirectApiClient(OdinId identity, IApiClientFactory factory)
{
    public async Task<ApiResponse<TransitResult>> TransferMetadata(
        TargetDrive remoteTargetDrive,
        UploadFileMetadata fileMetadata,
        List<string> recipients,
        Guid? overwriteGlobalTransitFileId,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        PeerDirectInstructionSet instructionSet = new PeerDirectInstructionSet()
        {
            TransferIv = transferIv,
            OverwriteGlobalTransitFileId = overwriteGlobalTransitFileId,
            RemoteTargetDrive = remoteTargetDrive,
            Recipients = recipients,
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

            var svc = RestService.For<IUniversalRefitPeerDirect>(client);
            ApiResponse<TransitResult> response = await svc.TransferStream(parts.ToArray());

            keyHeader.AesKey.Wipe();

            return response;
        }
    }

    public async Task<(ApiResponse<TransitResult> response,
            string encryptedJsonContent64,
            List<EncryptedAttachmentUploadResult> uploadedThumbnails,
            List<EncryptedAttachmentUploadResult> uploadedPayloads)>
        TransferNewEncryptedFile(
            TargetDrive remoteTargetDrive,
            UploadFileMetadata fileMetadata,
            List<OdinId> recipients,
            UploadManifest uploadManifest = null,
            List<TestPayloadDefinition> payloads = null,
            FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        var uploadedThumbnails = new List<EncryptedAttachmentUploadResult>();
        var uploadedPayloads = new List<EncryptedAttachmentUploadResult>();


        PeerDirectInstructionSet instructionSet = new PeerDirectInstructionSet()
        {
            TransferIv = transferIv,
            OverwriteGlobalTransitFileId = default,
            RemoteTargetDrive = remoteTargetDrive,
            Recipients = recipients.Select(d => d.DomainName).ToList(),
            Manifest = uploadManifest ?? new UploadManifest()
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
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata))
            ];

            // Encrypt and add payloads
            foreach (var payloadDefinition in payloads ?? [])
            {
                var payloadKeyHeader = new KeyHeader()
                {
                    Iv = payloadDefinition.Iv,
                    AesKey = new SensitiveByteArray(keyHeader.AesKey.GetKey())
                };

                var payloadCipher = payloadKeyHeader.EncryptDataAesAsStream(payloadDefinition.Content);
                parts.Add(new StreamPart(payloadCipher, payloadDefinition.Key, payloadDefinition.ContentType, Enum.GetName(MultipartUploadParts.Payload)));
                uploadedPayloads.Add(new EncryptedAttachmentUploadResult()
                {
                    Key = payloadDefinition.Key,
                    ContentType = payloadDefinition.ContentType,
                    EncryptedContent64 = payloadCipher.ToByteArray().ToBase64()
                });

                payloadCipher.Position = 0;

                foreach (var thumbnail in payloadDefinition.Thumbnails ?? new List<ThumbnailContent>())
                {
                    var thumbnailCipher = payloadKeyHeader.EncryptDataAesAsStream(thumbnail.Content);
                    var thumbnailKey = $"{payloadDefinition.Key}{thumbnail.PixelWidth}{thumbnail.PixelHeight}"; //hulk smash (it all together)
                    parts.Add(new StreamPart(thumbnailCipher, thumbnailKey, thumbnail.ContentType, Enum.GetName(MultipartUploadParts.Thumbnail)));

                    uploadedThumbnails.Add(new EncryptedAttachmentUploadResult()
                    {
                        Key = thumbnailKey,
                        ContentType = thumbnail.ContentType,
                        EncryptedContent64 = thumbnailCipher.ToByteArray().ToBase64()
                    });

                    thumbnailCipher.Position = 0;
                }
            }

            var peerDirectSvc = RestService.For<IUniversalRefitPeerDirect>(client);
            ApiResponse<TransitResult> response = await peerDirectSvc.TransferStream(parts.ToArray());

            return (response, encryptedJsonContent64, uploadedThumbnails, uploadedPayloads);
        }
    }


    public async Task<(ApiResponse<TransitResult> response,
            string encryptedJsonContent64,
            List<EncryptedAttachmentUploadResult> uploadedThumbnails,
            List<EncryptedAttachmentUploadResult> uploadedPayloads)>
        UpdateEncryptedRemoteFile(
            TargetDrive remoteTargetDrive,
            UploadFileMetadata fileMetadata,
            List<OdinId> recipients,
            Guid overwriteGlobalTransitFileId,
            StorageIntent storageIntent,
            UploadManifest uploadManifest = null,
            List<TestPayloadDefinition> payloads = null,
            FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        var uploadedThumbnails = new List<EncryptedAttachmentUploadResult>();
        var uploadedPayloads = new List<EncryptedAttachmentUploadResult>();

        PeerDirectInstructionSet instructionSet = new PeerDirectInstructionSet()
        {
            TransferIv = transferIv,
            OverwriteGlobalTransitFileId = overwriteGlobalTransitFileId,
            RemoteTargetDrive = remoteTargetDrive,
            Recipients = recipients.Select(d => d.DomainName).ToList(),
            Manifest = uploadManifest ?? new UploadManifest(),
            StorageIntent = storageIntent
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
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata))
            ];

            // Encrypt and add payloads
            foreach (var payloadDefinition in payloads ?? [])
            {
                var payloadKeyHeader = new KeyHeader()
                {
                    Iv = payloadDefinition.Iv,
                    AesKey = new SensitiveByteArray(keyHeader.AesKey.GetKey())
                };

                var payloadCipher = payloadKeyHeader.EncryptDataAesAsStream(payloadDefinition.Content);
                parts.Add(new StreamPart(payloadCipher, payloadDefinition.Key, payloadDefinition.ContentType, Enum.GetName(MultipartUploadParts.Payload)));
                uploadedPayloads.Add(new EncryptedAttachmentUploadResult()
                {
                    Key = payloadDefinition.Key,
                    ContentType = payloadDefinition.ContentType,
                    EncryptedContent64 = payloadCipher.ToByteArray().ToBase64()
                });

                payloadCipher.Position = 0;

                foreach (var thumbnail in payloadDefinition.Thumbnails ?? new List<ThumbnailContent>())
                {
                    var thumbnailCipher = payloadKeyHeader.EncryptDataAesAsStream(thumbnail.Content);
                    var thumbnailKey = $"{payloadDefinition.Key}{thumbnail.PixelWidth}{thumbnail.PixelHeight}"; //hulk smash (it all together)
                    parts.Add(new StreamPart(thumbnailCipher, thumbnailKey, thumbnail.ContentType, Enum.GetName(MultipartUploadParts.Thumbnail)));

                    uploadedThumbnails.Add(new EncryptedAttachmentUploadResult()
                    {
                        Key = thumbnailKey,
                        ContentType = thumbnail.ContentType,
                        EncryptedContent64 = thumbnailCipher.ToByteArray().ToBase64()
                    });

                    thumbnailCipher.Position = 0;
                }
            }

            var peerDirectSvc = RestService.For<IUniversalRefitPeerDirect>(client);
            ApiResponse<TransitResult> response = await peerDirectSvc.TransferStream(parts.ToArray());

            return (response, encryptedJsonContent64, uploadedThumbnails, uploadedPayloads);
        }
    }


    public async Task<ApiResponse<TransitResult>> TransferNewFile(
        TargetDrive remoteTargetDrive,
        UploadFileMetadata fileMetadata,
        List<OdinId> recipients,
        UploadManifest uploadManifest = null,
        List<TestPayloadDefinition> payloads = null,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        PeerDirectInstructionSet instructionSet = new PeerDirectInstructionSet()
        {
            TransferIv = transferIv,
            OverwriteGlobalTransitFileId = default,
            RemoteTargetDrive = remoteTargetDrive,
            Recipients = recipients.Select(d => d.DomainName).ToList(),
            Manifest = uploadManifest ?? new UploadManifest()
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
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata))
            ];

            foreach (var payloadDefinition in payloads ?? [])
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

            var peerDirectSvc = RestService.For<IUniversalRefitPeerDirect>(client);
            ApiResponse<TransitResult> response = await peerDirectSvc.TransferStream(parts.ToArray());

            keyHeader.AesKey.Wipe();

            return response;
        }
    }

    public async Task<ApiResponse<TransitResult>> UpdateRemoteFile(
        TargetDrive remoteTargetDrive,
        UploadFileMetadata fileMetadata,
        List<OdinId> recipients,
        Guid? overwriteGlobalTransitFileId,
        StorageIntent storageIntent,
        UploadManifest uploadManifest = null,
        List<TestPayloadDefinition> payloads = null,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        PeerDirectInstructionSet instructionSet = new PeerDirectInstructionSet()
        {
            TransferIv = transferIv,
            OverwriteGlobalTransitFileId = overwriteGlobalTransitFileId,
            RemoteTargetDrive = remoteTargetDrive,
            Recipients = recipients.Select(d => d.DomainName).ToList(),
            Manifest = uploadManifest ?? new UploadManifest(),
            StorageIntent = storageIntent
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
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata))
            ];

            foreach (var payloadDefinition in payloads ?? [])
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

            var peerDirectSvc = RestService.For<IUniversalRefitPeerDirect>(client);
            ApiResponse<TransitResult> response = await peerDirectSvc.TransferStream(parts.ToArray());

            keyHeader.AesKey.Wipe();

            return response;
        }
    }

    public async Task<ApiResponse<DeleteFileResult>> DeleteFile(FileSystemType fileSystemType,
        GlobalTransitIdFileIdentifier remoteGlobalTransitIdFileIdentifier,
        List<OdinId> recipients)
    {
        var request = new DeleteFileByGlobalTransitIdRequest()
        {
            FileSystemType = fileSystemType,
            GlobalTransitIdFileIdentifier = remoteGlobalTransitIdFileIdentifier,
            Recipients = recipients.Select(d => d.DomainName).ToList(),
        };

        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        {
            var transitSvc = RefitCreator.RestServiceFor<IUniversalRefitPeerDirect>(client, sharedSecret);
            var response = await transitSvc.SendDeleteRequest(request);
            return response;
        }
    }

    public async Task<(ApiResponse<TransitResult> transitResultResponse, string encryptedJsonContent64)> TransferEncryptedMetadata(
        TargetDrive remoteTargetDrive,
        UploadFileMetadata fileMetadata,
        List<string> recipients,
        Guid? overwriteGlobalTransitFileId,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        PeerDirectInstructionSet instructionSet = new PeerDirectInstructionSet()
        {
            TransferIv = transferIv,
            OverwriteGlobalTransitFileId = overwriteGlobalTransitFileId,
            RemoteTargetDrive = remoteTargetDrive,
            Recipients = recipients,
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

            var transitSvc = RestService.For<IUniversalRefitPeerDirect>(client);
            ApiResponse<TransitResult> transitResultResponse = await transitSvc.TransferStream(parts.ToArray());

            keyHeader.AesKey.Wipe();

            return (transitResultResponse, encryptedJsonContent64);
        }
    }

    public async Task<ApiResponse<UploadPayloadResult>> UploadPayloads(
        Guid targetGlobalTransitId,
        Guid targetVersionTag,
        TargetDrive remoteTargetDrive,
        UploadManifest uploadManifest,
        List<TestPayloadDefinition> payloads,
        List<string> recipients,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var instructionSet = new PeerDirectUploadPayloadInstructionSet()
        {
            TargetFile = new FileIdentifier()
            {
                FileId = targetGlobalTransitId,
                Drive = remoteTargetDrive,
                Type = FileIdentifierType.GlobalTransitId
            },
            Recipients = recipients,
            VersionTag = targetVersionTag,
            Manifest = uploadManifest
        };

        var instructionSetBytes = OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray();

        List<StreamPart> parts =
        [
            new StreamPart(new MemoryStream(instructionSetBytes), "instructionSet", "application/json",
                Enum.GetName(MultipartUploadParts.PayloadUploadInstructions))
        ];

        foreach (var payloadDefinition in payloads)
        {
            parts.Add(new StreamPart(new MemoryStream(payloadDefinition.Content), payloadDefinition.Key, payloadDefinition.ContentType,
                Enum.GetName(MultipartUploadParts.Payload)));

            foreach (var thumbnail in payloadDefinition.Thumbnails)
            {
                var thumbnailKey = thumbnail.GetUploadKey(payloadDefinition.Key);
                parts.Add(new StreamPart(new MemoryStream(thumbnail.Content), thumbnailKey, thumbnail.ContentType,
                    Enum.GetName(MultipartUploadParts.Thumbnail)));
            }
        }

        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        {
            var svc = RestService.For<IUniversalRefitPeerDirect>(client);
            var response = await svc.UploadPayload(parts.ToArray());
            return response;
        }
    }

    public async Task<(ApiResponse<UploadPayloadResult> response, Dictionary<string, byte[]> encryptedPayloads64)> UploadEncryptedPayloads(
        Guid targetGlobalTransitId,
        Guid targetVersionTag,
        TargetDrive remoteTargetDrive,
        UploadManifest uploadManifest,
        List<TestPayloadDefinition> payloads,
        List<string> recipients,
        byte[] aesKey,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var instructionSet = new PeerDirectUploadPayloadInstructionSet()
        {
            TargetFile = new FileIdentifier()
            {
                FileId = targetGlobalTransitId,
                Drive = remoteTargetDrive,
                Type = FileIdentifierType.GlobalTransitId
            },
            Recipients = recipients,
            VersionTag = targetVersionTag,
            Manifest = uploadManifest
        };

        var instructionSetBytes = OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray();

        List<StreamPart> parts =
        [
            new StreamPart(new MemoryStream(instructionSetBytes), "instructionSet", "application/json",
                Enum.GetName(MultipartUploadParts.PayloadUploadInstructions))
        ];

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
                var thumbnailKey = thumbnail.GetUploadKey(payloadDefinition.Key);
                parts.Add(new StreamPart(encryptedThumbnailStream, thumbnailKey, thumbnail.ContentType,
                    Enum.GetName(MultipartUploadParts.Thumbnail)));
            }
        }

        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        {
            var svc = RestService.For<IUniversalRefitPeerDirect>(client);
            var response = await svc.UploadPayload(parts.ToArray());
            return (response, encryptedPayloads64);
        }
    }

    public async Task<ApiResponse<PeerDeletePayloadResult>> DeletePayload(Guid targetGlobalTransitId,
        Guid targetVersionTag, TargetDrive recipientTargetDrive, string payloadKey, List<string> recipients,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        {
            var svc = RefitCreator.RestServiceFor<IUniversalRefitPeerDirect>(client, sharedSecret);
            var response = await svc.DeletePayload(new PeerDeletePayloadRequest
            {
                Key = payloadKey,
                File = new FileIdentifier()
                {
                    FileId = targetGlobalTransitId,
                    Drive = recipientTargetDrive,
                    Type = FileIdentifierType.GlobalTransitId
                },
                VersionTag = targetVersionTag,
                Recipients = recipients
            });

            return response;
        }
    }
}