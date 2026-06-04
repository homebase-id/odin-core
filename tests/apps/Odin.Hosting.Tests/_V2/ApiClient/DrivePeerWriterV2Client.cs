using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

/// <summary>
/// V2 client for the "write over peer" endpoint (<c>POST /api/v2/peer/{odinId}/drives/{driveId}/files/send</c>).
/// Builds the same multipart bundle V1 transit send uses (TransitInstructionSet + metadata descriptor +
/// payloads); the file lands on the remote owner's drive with no local copy.
/// </summary>
public class DrivePeerWriterV2Client(OdinId identity, IApiClientFactory factory, FileSystemType fileSystemType = FileSystemType.Standard)
{
    public async Task<ApiResponse<TransitResult>> SendUnencryptedFileOverPeer(
        OdinId recipient,
        TargetDrive remoteTargetDrive,
        UploadFileMetadata fileMetadata,
        List<TestPayloadDefinition> payloads = null,
        UploadManifest manifest = null,
        Guid? overwriteGlobalTransitFileId = null)
    {
        payloads ??= new List<TestPayloadDefinition>();
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        var instructionSet = new TransitInstructionSet
        {
            TransferIv = transferIv,
            Recipients = new List<string> { recipient },
            RemoteTargetDrive = remoteTargetDrive,
            OverwriteGlobalTransitFileId = overwriteGlobalTransitFileId,
            Manifest = manifest
        };

        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);

        var instructionStream = new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

        var descriptor = new UploadFileDescriptor
        {
            EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref sharedSecret),
            FileMetadata = fileMetadata
        };

        var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref sharedSecret);

        List<StreamPart> parts =
        [
            new StreamPart(instructionStream, "instructionSet.encrypted", "application/json",
                Enum.GetName(MultipartUploadParts.Instructions)),

            new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json",
                Enum.GetName(MultipartUploadParts.Metadata))
        ];

        foreach (var payloadDefinition in payloads)
        {
            parts.Add(new StreamPart(new MemoryStream(payloadDefinition.Content), payloadDefinition.Key,
                payloadDefinition.ContentType, Enum.GetName(MultipartUploadParts.Payload)));

            foreach (var thumbnail in payloadDefinition.Thumbnails ?? new List<ThumbnailContent>())
            {
                var thumbnailKey = $"{payloadDefinition.Key}{thumbnail.PixelWidth}{thumbnail.PixelHeight}";
                parts.Add(new StreamPart(new MemoryStream(thumbnail.Content), thumbnailKey, thumbnail.ContentType,
                    Enum.GetName(MultipartUploadParts.Thumbnail)));
            }
        }

        var svc = RestService.For<IDrivePeerWriteHttpClientApiV2>(client);
        var response = await svc.SendFile(recipient.DomainName, remoteTargetDrive.Alias, parts.ToArray());

        keyHeader.AesKey.Wipe();
        return response;
    }

    public async Task<ApiResponse<TransitResult>> SendEncryptedFileOverPeer(
        OdinId recipient,
        TargetDrive remoteTargetDrive,
        UploadFileMetadata fileMetadata,
        KeyHeader keyHeader,
        List<TestPayloadDefinition> payloads = null,
        UploadManifest manifest = null,
        Guid? overwriteGlobalTransitFileId = null)
    {
        payloads ??= new List<TestPayloadDefinition>();
        var transferIv = ByteArrayUtil.GetRndByteArray(16);

        var instructionSet = new TransitInstructionSet
        {
            TransferIv = transferIv,
            Recipients = new List<string> { recipient },
            RemoteTargetDrive = remoteTargetDrive,
            OverwriteGlobalTransitFileId = overwriteGlobalTransitFileId,
            Manifest = manifest
        };

        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);

        var instructionStream = new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

        // Encrypt the metadata content with the file key header.
        var encryptedContent64 = keyHeader.EncryptDataAes(fileMetadata.AppData.Content.ToUtf8ByteArray()).ToBase64();
        fileMetadata.AppData.Content = encryptedContent64;
        fileMetadata.IsEncrypted = true;

        var descriptor = new UploadFileDescriptor
        {
            EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref sharedSecret),
            FileMetadata = fileMetadata
        };

        var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref sharedSecret);

        List<StreamPart> parts =
        [
            new StreamPart(instructionStream, "instructionSet.encrypted", "application/json",
                Enum.GetName(MultipartUploadParts.Instructions)),

            new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json",
                Enum.GetName(MultipartUploadParts.Metadata))
        ];

        foreach (var payload in payloads)
        {
            var payloadKeyHeader = new KeyHeader
            {
                Iv = payload.Iv,
                AesKey = new SensitiveByteArray(keyHeader.AesKey.GetKey())
            };

            var payloadCipher = payloadKeyHeader.EncryptDataAesAsStream(payload.Content);
            parts.Add(new StreamPart(payloadCipher, payload.Key, payload.ContentType,
                Enum.GetName(MultipartUploadParts.Payload)));

            foreach (var thumb in payload.Thumbnails ?? new List<ThumbnailContent>())
            {
                var thumbCipher = payloadKeyHeader.EncryptDataAesAsStream(thumb.Content);
                var thumbKey = $"{payload.Key}{thumb.PixelWidth}{thumb.PixelHeight}";
                parts.Add(new StreamPart(thumbCipher, thumbKey, thumb.ContentType,
                    Enum.GetName(MultipartUploadParts.Thumbnail)));
            }
        }

        var svc = RestService.For<IDrivePeerWriteHttpClientApiV2>(client);
        var response = await svc.SendFile(recipient.DomainName, remoteTargetDrive.Alias, parts.ToArray());

        keyHeader.AesKey.Wipe();
        return response;
    }

    public async Task<ApiResponse<Dictionary<string, DeleteLinkedFileStatus>>> SendDeleteRequestOverPeer(
        OdinId recipient, TargetDrive remoteTargetDrive, Guid globalTransitId)
    {
        var client = factory.CreateHttpClient(identity, out _, fileSystemType);

        var request = new DeleteFileByGlobalTransitIdRequest
        {
            FileSystemType = fileSystemType,
            GlobalTransitIdFileIdentifier = new GlobalTransitIdFileIdentifier
            {
                GlobalTransitId = globalTransitId,
                TargetDrive = remoteTargetDrive
            },
            Recipients = new List<string> { recipient }
        };

        var svc = RestService.For<IDrivePeerWriteHttpClientApiV2>(client);
        return await svc.SendDeleteRequest(recipient.DomainName, remoteTargetDrive.Alias, request);
    }
}
