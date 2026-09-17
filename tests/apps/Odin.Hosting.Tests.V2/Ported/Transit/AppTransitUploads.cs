using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.Transit;

/// <summary>
/// The upload the <c>AppAPI/Transit</c> fixtures arrange with: one unencrypted file carrying a random
/// unique id, optionally one plaintext payload and one thumbnail hanging off it.
/// </summary>
/// <remarks>
/// Stands in for the originals' <c>DriveApiClient.UploadFile(fst, drive, metadata, payloadData,
/// thumbnail, payloadKey)</c>, which has no <c>_Universal</c> twin; the <c>_Universal</c> spelling is
/// <c>UploadNewFile</c> with an explicit manifest, as in
/// <see cref="TransitBadCATDetectionTests"/>. One wire-level difference, inert for every assertion
/// here: the two spellings name thumbnail parts differently — the original's
/// <c>ThumbnailDescriptor.GetFilename</c> produces <c>{w}x{h}-{payloadKey}</c>, while
/// <c>UploadNewFile</c> and <c>TestPayloadDefinition.ToPayloadDescriptor</c> agree on
/// <c>{payloadKey}{w}{h}</c>. Nothing reads that key: the transit thumbnail request addresses a
/// thumbnail by payload key plus pixel dimensions.
/// <para>
/// The three fixtures wrap this with their own ACL, keeping the original helper names
/// (<c>UploadStandardRandomPublicFileHeader</c> / <c>UploadStandardRandomSecureConnectedFile</c>).
/// </para>
/// </remarks>
internal static class AppTransitUploads
{
    public static async Task<(UploadResult uploadResult, UploadFileMetadata uploadedMetadata)> UploadStandardRandomFileAsync(
        OwnerSession owner,
        TargetDrive targetDrive,
        AccessControlList acl,
        string payload = null,
        ThumbnailContent thumbnail = null)
    {
        var fileMetadata = new UploadFileMetadata
        {
            IsEncrypted = false,
            AppData = new UploadAppFileMetaData
            {
                FileType = 777,
                Content = $"some json content {Guid.NewGuid()}",
                UniqueId = Guid.NewGuid()
            },
            AccessControlList = acl
        };

        var payloads = new List<TestPayloadDefinition>();
        if (payload != null)
        {
            payloads.Add(new TestPayloadDefinition
            {
                Iv = null,
                Key = WebScaffold.PAYLOAD_KEY,
                ContentType = "application/x-binary",
                Content = payload.ToUtf8ByteArray(),
                DescriptorContent = "",
                PreviewThumbnail = default,
                Thumbnails = thumbnail == null ? [] : [thumbnail]
            });
        }

        var manifest = new UploadManifest { PayloadDescriptors = payloads.ToPayloadDescriptorList().ToList() };

        var response = await owner.V1.Drive.UploadNewFile(targetDrive, fileMetadata, manifest, payloads);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (response.Content, fileMetadata);
    }

    /// <summary>
    /// The originals' <c>ModifyFile</c>: read the header, append to its content, and write the
    /// metadata back over the same file. Returns the upload result and the re-read metadata.
    /// </summary>
    /// <remarks>
    /// The original overwrote the whole file (<c>UploadFile(..., overwriteFileId:)</c> with no
    /// payload part), which drops any payload the file had; the <c>_Universal</c>
    /// <c>UpdateExistingMetadata</c> used here declares <c>StorageIntent.MetadataOnly</c> and keeps
    /// it. No caller reads the payload after modifying, so the difference is inert — recorded rather
    /// than hidden.
    /// </remarks>
    public static async Task<(UploadResult uploadResult, ClientFileMetadata modifiedMetadata)> ModifyFileAsync(
        OwnerSession owner, ExternalFileIdentifier file)
    {
        var header = await owner.V1.Drive.GetFileHeader(file);
        Assert.That(header.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var fileMetadata = new UploadFileMetadata
        {
            IsEncrypted = false,
            AppData = new UploadAppFileMetaData
            {
                FileType = 777,
                Content = header.Content!.FileMetadata.AppData.Content + " something i appended"
            },
            AccessControlList = AccessControlList.Anonymous
        };

        var result = await owner.V1.Drive.UpdateExistingMetadata(file, header.Content.FileMetadata.VersionTag, fileMetadata);
        Assert.That(result.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var modifiedFile = await owner.V1.Drive.GetFileHeader(file);
        Assert.That(modifiedFile.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (result.Content, modifiedFile.Content!.FileMetadata);
    }
}
