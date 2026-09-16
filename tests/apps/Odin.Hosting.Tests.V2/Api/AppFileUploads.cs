#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Hosting.Tests;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;
using Refit;

namespace Odin.Hosting.Tests.V2.Api;

/// <summary>
/// The encrypted, one-payload upload that <c>AppApiTestUtils.CreateAppAndUploadFileMetadata</c> did
/// for the local (no-recipient) case, as an arrange step an App caller can run directly. Shared
/// test vocabulary in the spirit of <see cref="Peer.PeerFlow"/> rather than a client facade.
/// </summary>
/// <remarks>
/// Exists because two ported <c>AppAPI/Drive</c> fixtures need this exact body: the payload is
/// encrypted with a fresh <see cref="KeyHeader"/> under a per-payload IV, the metadata is forced to
/// <c>IsEncrypted = true</c>, and the optional 300x300 thumbnail is encrypted with the same key
/// header. Faithful to the original down to the manifest shape and part names, which is what makes
/// the round-trip assertions in those fixtures meaningful.
///
/// What the original <i>also</i> did and this does not: register a circle granting the drive
/// (<c>CreateCircleWithDrive</c>) and set up recipient identities. Neither is reachable from a local
/// upload with no <c>TransitOptions.Recipients</c>.
/// </remarks>
public static class AppFileUploads
{
    /// <summary>What the upload produced — enough for a caller to read the file back and decrypt it.</summary>
    public sealed record UploadedFile(
        UploadResult UploadResult,
        KeyHeader KeyHeader,
        string PayloadData,
        IReadOnlyList<ThumbnailDescriptor> Thumbnails);

    /// <summary>
    /// Uploads one encrypted file to <paramref name="targetDrive"/> as <paramref name="caller"/>.
    /// Throws (via NUnit assertion) if the upload is refused — this is arrange, not the SUT.
    /// </summary>
    public static async Task<UploadedFile> UploadEncryptedAsync(
        IV2Caller caller,
        TargetDrive targetDrive,
        UploadFileMetadata fileMetadata,
        string payloadData = "{payload:true, image:'b64 data'}",
        bool includeThumbnail = false)
    {
        if (string.IsNullOrEmpty(payloadData))
        {
            throw new ArgumentException("Test data error - this arrange always uploads a payload", nameof(payloadData));
        }

        var keyHeader = KeyHeader.NewRandom16();
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        const string payloadKey = WebScaffold.PAYLOAD_KEY;

        fileMetadata.IsEncrypted = true;

        var thumbnailParts = new List<StreamPart>();
        var thumbnailsAdded = new List<ThumbnailDescriptor>();
        var thumbs = new List<UploadedManifestThumbnailDescriptor>();

        if (includeThumbnail)
        {
            var thumbnail1 = new ThumbnailDescriptor()
            {
                PixelHeight = 300,
                PixelWidth = 300,
                ContentType = "image/jpeg"
            };

            thumbs.Add(new UploadedManifestThumbnailDescriptor()
            {
                PixelHeight = thumbnail1.PixelHeight,
                PixelWidth = thumbnail1.PixelWidth,
                ThumbnailKey = thumbnail1.GetFilename(payloadKey)
            });

            var thumbnail1CipherBytes = keyHeader.EncryptDataAes(TestMedia.ThumbnailBytes300);
            thumbnailParts.Add(new StreamPart(new MemoryStream(thumbnail1CipherBytes), thumbnail1.GetFilename(payloadKey),
                thumbnail1.ContentType, Enum.GetName(MultipartUploadParts.Thumbnail)));
            thumbnailsAdded.Add(thumbnail1);
        }

        var instructionSet = new UploadInstructionSet()
        {
            TransferIv = transferIv,
            StorageOptions = new StorageOptions()
            {
                Drive = targetDrive,
                OverwriteFileId = null
            },
            TransitOptions = null,
            Manifest = new UploadManifest()
            {
                PayloadDescriptors = new List<UploadManifestPayloadDescriptor>()
                {
                    new()
                    {
                        Iv = ByteArrayUtil.GetRndByteArray(16),
                        PayloadKey = payloadKey,
                        Thumbnails = thumbs
                    }
                }
            }
        };

        var http = caller.Factory.CreateHttpClient(caller.Identity, out var sharedSecret);
        var instructionStream = new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

        var descriptor = new UploadFileDescriptor()
        {
            EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref sharedSecret),
            FileMetadata = fileMetadata
        };

        var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref sharedSecret);
        var payloadCipher = keyHeader.EncryptDataAesAsStream(payloadData);

        var parts = new List<StreamPart>
        {
            new(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
            new(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
            new(payloadCipher, payloadKey, "application/x-binary", Enum.GetName(MultipartUploadParts.Payload))
        };
        parts.AddRange(thumbnailParts);

        var svc = RestService.For<IUniversalDriveHttpClientApi>(http);
        var response = await svc.UploadStream(parts.ToArray());

        Assert.That(response.IsSuccessStatusCode, Is.True, $"app upload failed: {response.StatusCode}");
        Assert.That(response.Content, Is.Not.Null);

        var uploadResult = response.Content!;
        Assert.That(uploadResult.File, Is.Not.Null);
        Assert.That(uploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(uploadResult.File.TargetDrive.IsValid(), Is.True);

        return new UploadedFile(uploadResult, keyHeader, payloadData, thumbnailsAdded);
    }
}
