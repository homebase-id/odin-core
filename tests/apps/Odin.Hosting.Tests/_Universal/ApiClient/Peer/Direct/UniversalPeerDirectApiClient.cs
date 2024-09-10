using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
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

        TransitInstructionSet instructionSet = new TransitInstructionSet()
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
            ApiResponse<TransitResult> response = await svc.UploadFile(parts.ToArray());

            keyHeader.AesKey.Wipe();

            return response;
        }
    }

    public async Task<ApiResponse<TransitResult>> TransferNewFile(
        TargetDrive remoteTargetDrive,
        UploadFileMetadata fileMetadata,
        List<OdinId> recipients,
        Guid? overwriteGlobalTransitFileId,
        UploadManifest uploadManifest,
        List<TestPayloadDefinition> payloads,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        TransitInstructionSet instructionSet = new TransitInstructionSet()
        {
            TransferIv = transferIv,
            OverwriteGlobalTransitFileId = overwriteGlobalTransitFileId,
            RemoteTargetDrive = remoteTargetDrive,
            Recipients = recipients.Select(d => d.DomainName).ToList(),
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
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata))
            ];

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

            var driveSvc = RestService.For<IUniversalRefitPeerDirect>(client);
            ApiResponse<TransitResult> response = await driveSvc.UploadFile(parts.ToArray());

            keyHeader.AesKey.Wipe();

            return response;
        }
    }

    public async Task<ApiResponse<TransitResult>> UpdateFile(
        PeerUpdateInstructionSet uploadInstructionSet,
        UploadFileMetadata fileMetadata,
        List<TestPayloadDefinition> payloads,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var keyHeader = KeyHeader.NewRandom16();

        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        {
            var instructionStream = new MemoryStream(OdinSystemSerializer.Serialize(uploadInstructionSet).ToUtf8ByteArray());
            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, uploadInstructionSet.TransferIv, ref sharedSecret),
                FileMetadata = fileMetadata
            };

            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, uploadInstructionSet.TransferIv, ref sharedSecret);

            List<StreamPart> parts =
            [
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata))
            ];

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

            var driveSvc = RestService.For<IUniversalRefitPeerDirect>(client);
            ApiResponse<TransitResult> response = await driveSvc.UpdateFile(parts.ToArray());

            keyHeader.AesKey.Wipe();

            return response;
        }
    }

    public async Task DeleteFile(FileSystemType fileSystemType, GlobalTransitIdFileIdentifier remoteGlobalTransitIdFileIdentifier,
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

        TransitInstructionSet instructionSet = new TransitInstructionSet()
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
            ApiResponse<TransitResult> transitResultResponse = await transitSvc.UploadFile(parts.ToArray());

            keyHeader.AesKey.Wipe();

            return (transitResultResponse, encryptedJsonContent64);
        }
    }
}