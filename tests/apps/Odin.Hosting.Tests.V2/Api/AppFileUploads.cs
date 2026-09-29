#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
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
/// The encrypted multipart upload the ported V1 drive fixtures arrange with, as a step any caller
/// (Owner / App / Guest) can run directly. Shared test vocabulary in the spirit of
/// <see cref="Peer.PeerFlow"/> rather than a client facade.
/// </summary>
/// <remarks>
/// Exists because a dozen ported fixtures need this exact body: the payload is encrypted with a
/// <see cref="KeyHeader"/> under a per-payload IV, the metadata is forced to <c>IsEncrypted = true</c>,
/// and thumbnails are encrypted with the same key header. Faithful to the originals down to the
/// manifest shape and part names, which is what makes the round-trip assertions in those fixtures
/// meaningful.
///
/// What the originals <i>also</i> did and this does not: register a circle granting the drive
/// (<c>CreateCircleWithDrive</c>) and set up recipient identities. Neither is reachable from a local
/// upload with no <c>TransitOptions.Recipients</c>.
/// </remarks>
public static class AppFileUploads
{
    /// <summary>The payload body the originals sent when they did not care what was in it.</summary>
    public const string DefaultPayloadData = "{payload:true, image:'b64 data'}";

    /// <summary>A thumbnail as it went on the wire: the descriptor the manifest named, and the cipher bytes.</summary>
    public sealed record UploadedThumbnail(ThumbnailDescriptor Descriptor, byte[] CipherBytes);

    /// <summary>
    /// What the upload sent and what came back — enough for a caller to read the file back, decrypt
    /// it, and compare the stored bytes against the bytes it put on the wire.
    /// </summary>
    public sealed record UploadedFile(
        ApiResponse<UploadResult> Response,
        KeyHeader KeyHeader,
        string? PayloadData,
        byte[]? PayloadCipher,
        IReadOnlyList<UploadedThumbnail> Thumbnails)
    {
        /// <summary>The upload result. Only meaningful once the upload is known to have succeeded.</summary>
        public UploadResult UploadResult => Response.Content!;
    }

    /// <summary>
    /// Uploads one encrypted file to <paramref name="targetDrive"/> as <paramref name="caller"/> and
    /// asserts it was accepted — this is arrange, not the SUT. Use
    /// <see cref="TryUploadEncryptedAsync"/> where the refusal is the thing under test.
    /// </summary>
    public static async Task<UploadedFile> UploadEncryptedAsync(
        IV2Caller caller,
        TargetDrive targetDrive,
        UploadFileMetadata fileMetadata,
        string? payloadData = DefaultPayloadData,
        IReadOnlyList<int>? thumbnailSizes = null,
        byte[]? payloadIv = null,
        Guid? overwriteFileId = null,
        KeyHeader? keyHeader = null)
    {
        var uploaded = await TryUploadEncryptedAsync(caller, targetDrive, fileMetadata, payloadData, thumbnailSizes,
            payloadIv, overwriteFileId, keyHeader);

        Assert.That(uploaded.Response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(uploaded.Response.Content, Is.Not.Null);

        var uploadResult = uploaded.UploadResult;
        Assert.That(uploadResult.File, Is.Not.Null);
        Assert.That(uploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(uploadResult.File.TargetDrive.IsValid(), Is.True);

        return uploaded;
    }

    /// <summary>
    /// <see cref="UploadEncryptedAsync"/> without the assertions: returns whatever the server said.
    /// </summary>
    /// <param name="payloadData">
    /// The payload plaintext, or <c>null</c> for a metadata-only upload (no payload part, no payload
    /// descriptor in the manifest).
    /// </param>
    /// <param name="thumbnailSizes">
    /// Square thumbnail sizes to attach to the payload; each is sourced from <see cref="TestMedia"/>
    /// and encrypted with the file's key header.
    /// </param>
    /// <param name="payloadIv">
    /// When given, the payload is encrypted under this IV and the manifest declares it, so the caller
    /// can decrypt what it fetches back with the same IV. When omitted the payload is encrypted under
    /// the file key header's own IV and the manifest carries an unrelated random IV — the shape most
    /// of the originals sent.
    /// </param>
    /// <param name="keyHeader">
    /// The file key header to use. Pass one in when the metadata itself must be built with it (e.g. an
    /// encrypted preview thumbnail); otherwise a fresh one is generated and returned.
    /// </param>
    public static async Task<UploadedFile> TryUploadEncryptedAsync(
        IV2Caller caller,
        TargetDrive targetDrive,
        UploadFileMetadata fileMetadata,
        string? payloadData = DefaultPayloadData,
        IReadOnlyList<int>? thumbnailSizes = null,
        byte[]? payloadIv = null,
        Guid? overwriteFileId = null,
        KeyHeader? keyHeader = null)
    {
        if (payloadData == null && thumbnailSizes is { Count: > 0 })
        {
            throw new ArgumentException("Test data error - thumbnails hang off a payload", nameof(thumbnailSizes));
        }

        keyHeader ??= KeyHeader.NewRandom16();
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        const string payloadKey = WebScaffold.PAYLOAD_KEY;

        fileMetadata.IsEncrypted = true;

        var thumbnailParts = new List<StreamPart>();
        var thumbnails = new List<UploadedThumbnail>();
        var manifestThumbnails = new List<UploadedManifestThumbnailDescriptor>();

        foreach (var size in thumbnailSizes ?? [])
        {
            var thumbnail = new ThumbnailDescriptor()
            {
                PixelHeight = size,
                PixelWidth = size,
                ContentType = "image/jpeg"
            };

            var thumbnailKey = thumbnail.GetFilename(payloadKey);
            var thumbnailCipherBytes = keyHeader.EncryptDataAes(ThumbnailSource(size));

            manifestThumbnails.Add(new UploadedManifestThumbnailDescriptor()
            {
                PixelHeight = thumbnail.PixelHeight,
                PixelWidth = thumbnail.PixelWidth,
                ThumbnailKey = thumbnailKey
            });

            thumbnailParts.Add(new StreamPart(new MemoryStream(thumbnailCipherBytes), thumbnailKey,
                thumbnail.ContentType, Enum.GetName(MultipartUploadParts.Thumbnail)));
            thumbnails.Add(new UploadedThumbnail(thumbnail, thumbnailCipherBytes));
        }

        var payloadDescriptors = new List<UploadManifestPayloadDescriptor>();
        Stream? payloadCipherStream = null;
        byte[]? payloadCipher = null;

        if (payloadData != null)
        {
            var payloadKeyHeader = payloadIv == null
                ? keyHeader
                : new KeyHeader() { Iv = payloadIv, AesKey = keyHeader.AesKey };

            payloadCipherStream = payloadKeyHeader.EncryptDataAesAsStream(payloadData);
            payloadCipher = ((MemoryStream)payloadCipherStream).ToArray();

            payloadDescriptors.Add(new UploadManifestPayloadDescriptor()
            {
                Iv = payloadIv ?? ByteArrayUtil.GetRndByteArray(16),
                PayloadKey = payloadKey,
                Thumbnails = manifestThumbnails
            });
        }

        var instructionSet = new UploadInstructionSet()
        {
            TransferIv = transferIv,
            StorageOptions = new StorageOptions()
            {
                Drive = targetDrive,
                OverwriteFileId = overwriteFileId
            },
            // TransitOptions is left at the constructor default (an empty TransitOptions), which is what
            // every original sent. It is not the same as null: FileSystemStreamWriterBase.ProcessTransitBasic
            // returns an empty RecipientStatus dictionary for a null TransitOptions and a null one for an
            // empty TransitOptions, and two of these fixtures assert RecipientStatus is null.
            Manifest = new UploadManifest()
            {
                PayloadDescriptors = payloadDescriptors
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

        var parts = new List<StreamPart>
        {
            new(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
            new(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata))
        };

        if (payloadCipherStream != null)
        {
            parts.Add(new StreamPart(payloadCipherStream, payloadKey, "application/x-binary",
                Enum.GetName(MultipartUploadParts.Payload)));
        }

        parts.AddRange(thumbnailParts);

        var svc = RestService.For<IUniversalDriveHttpClientApi>(http);
        var response = await svc.UploadStream(parts.ToArray());

        return new UploadedFile(response, keyHeader, payloadData, payloadCipher, thumbnails);
    }

    private static byte[] ThumbnailSource(int pixelSize) => pixelSize switch
    {
        300 => TestMedia.ThumbnailBytes300,
        400 => TestMedia.ThumbnailBytes400,
        _ => throw new ArgumentException($"No test thumbnail source for {pixelSize}x{pixelSize}", nameof(pixelSize))
    };
}
