using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Core.Storage;
using Odin.Hosting.Tests.AppAPI.ApiClient.Base;
using Odin.Hosting.Tests.AppAPI.Utils;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Org.BouncyCastle.Pkcs;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.ApiClient.Transit.Files;

/// <summary>
/// Sends files over transit
/// </summary>
public class AppTransitSenderApiClient : AppApiClientBase
{
    private readonly AppClientToken _token;

    public AppTransitSenderApiClient(OwnerApiTestUtils ownerApiTestUtils, AppClientToken token) : base(ownerApiTestUtils)
    {
        _token = token;
    }

    public async Task<ApiResponse<TransitResult>> TransferFile(
        UploadFileMetadata fileMetadata,
        List<string> recipients,
        TargetDrive remoteTargetDrive,
        Guid? overwriteGlobalTransitFileId = null,
        string payloadData = "",
        ThumbnailContent thumbnail = null,
        FileSystemType fileSystemType = FileSystemType.Standard
    )
    {
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        TransitInstructionSet instructionSet = new TransitInstructionSet()
        {
            TransferIv = transferIv,
            OverwriteGlobalTransitFileId = overwriteGlobalTransitFileId,
            RemoteTargetDrive = remoteTargetDrive,
            Recipients = recipients,
            Manifest = new UploadManifest()
        };

        var sharedSecret = _token.SharedSecret.ToSensitiveByteArray();
        bool hasPayload = !string.IsNullOrEmpty(payloadData);

        if (hasPayload)
        {
            var m = new UploadManifestPayloadDescriptor()
            {
                PayloadKey = WebScaffold.PAYLOAD_KEY
            };

            if (thumbnail != null)
            {
                m.Thumbnails = new List<UploadedManifestThumbnailDescriptor>()
                {
                    new()
                    {
                        ThumbnailKey = thumbnail.GetFilename(WebScaffold.PAYLOAD_KEY),
                        PixelHeight = thumbnail.PixelHeight,
                        PixelWidth = thumbnail.PixelWidth
                    }
                };
            }

            instructionSet.Manifest.PayloadDescriptors.Add(m);
        }

        var client = CreateAppApiHttpClient(_token, fileSystemType);
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
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
            };

            if (hasPayload)
            {
                parts.Add(
                    new StreamPart(new MemoryStream(payloadData.ToUtf8ByteArray()), WebScaffold.PAYLOAD_KEY, "application/x-binary",
                        Enum.GetName(MultipartUploadParts.Payload)));


                if (thumbnail != null)
                {
                    var thumbnailCipherBytes = keyHeader.EncryptDataAesAsStream(thumbnail.Content);
                    parts.Add(
                        new StreamPart(thumbnailCipherBytes, thumbnail.GetFilename(), thumbnail.ContentType, Enum.GetName(MultipartUploadParts.Thumbnail)));
                }
            }

            var svc = RestService.For<IRefitAppTransitSender>(client);
            ApiResponse<TransitResult> response = await svc.TransferStream(parts.ToArray());
            keyHeader.AesKey.Wipe();
            return response;
        }
    }
}