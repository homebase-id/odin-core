using System.Collections.Generic;
using System.Linq;
using Odin.Core.Exceptions;
using Odin.Core.Time;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Drives.FileSystem.Base.Upload;

/// <summary>
/// Describes the content being uploaded
/// </summary>
public class UploadManifest
{
    public List<UploadManifestPayloadDescriptor> PayloadDescriptors { get; set; } = new();

    public void AssertIsValid()
    {
        //ensure no duplicate payload keys are in the descriptors
        var groupingByPayloadKey = this.PayloadDescriptors?.GroupBy(pd => pd.PayloadKey);
        if (groupingByPayloadKey?.Any(g => g.Count() > 1) ?? false)
        {
            throw new OdinClientException("Duplicate payload keys found in the upload manifest", OdinClientErrorCode.InvalidUpload);
        }

        if (this.PayloadDescriptors?.Any() ?? false)
        {
            foreach (var pd in this.PayloadDescriptors)
            {
                DriveFileUtility.AssertValidPayloadKey(pd.PayloadKey);
                if (string.IsNullOrEmpty(pd.ContentType?.Trim()))
                {
                    throw new OdinClientException(
                        "Payloads must include a valid contentType in the multi-part upload.",
                        OdinClientErrorCode.InvalidPayload);
                }

                var anyMissingThumbnailKey = pd.Thumbnails?.Any(thumb => string.IsNullOrEmpty(thumb.ThumbnailKey?.Trim())) ?? false;
                if (anyMissingThumbnailKey)
                {
                    throw new OdinClientException($"The payload key [{pd.PayloadKey}] has a thumbnail missing a thumbnailKey",
                        OdinClientErrorCode.InvalidUpload);
                }

                var anyMissingContentTypeOnThumbnail = pd.Thumbnails?.Any(thumb => string.IsNullOrEmpty(thumb.ContentType?.Trim())) ?? false;
                if (anyMissingContentTypeOnThumbnail)
                {
                    throw new OdinClientException($"The payload key [{pd.PayloadKey}] has a thumbnail missing a content type",
                        OdinClientErrorCode.InvalidUpload);
                }

                // the width and height of all thumbnails must be unique for a given payload key
                var hasDuplicates = pd.Thumbnails?.GroupBy(p => $"{p.PixelWidth}{p.PixelHeight}")
                    .Any(group => group.Count() > 1) ?? false;

                if (hasDuplicates)
                {
                    throw new OdinClientException($"You have duplicate thumbnails for the " +
                                                  $"payloadKey [{pd.PayloadKey}]. in the UploadManifest. " +
                                                  $"You can have multiple thumbnails per payload key, however, " +
                                                  $"the WidthXHeight must be unique.", OdinClientErrorCode.InvalidPayload);
                }
            }
        }
    }

    public UploadManifestPayloadDescriptor GetPayloadDescriptor(string key)
    {
        return this.PayloadDescriptors?.SingleOrDefault(pk => pk.PayloadKey == key);
    }
}

/// <summary>
/// Describes the attributes of the payload of a given PayloadKey
/// </summary>
public class UploadManifestPayloadDescriptor
{
    public byte[] Iv { get; set; }
    public string PayloadKey { get; set; }
    public string DescriptorContent { get; set; }

    public string ContentType { get; set; }

    public ThumbnailContent PreviewThumbnail { get; set; }

    /// <summary>
    /// The thumbnails expected for this payload key
    /// </summary>
    public IEnumerable<UploadedManifestThumbnailDescriptor> Thumbnails { get; set; }

    public UnixTimeUtcUnique PayloadUid { get; set; }

    public PayloadDescriptor ToPayloadDescriptor()
    {
        return new PayloadDescriptor
        {
            Iv = this.Iv,
            Key = this.PayloadKey,
            ContentType = this.ContentType,
            DescriptorContent = this.DescriptorContent,
            BytesWritten = 0,
            LastModified = default,
            PreviewThumbnail = this.PreviewThumbnail,
            Thumbnails = this.Thumbnails.Select(t => t.ToThumbnailDescriptor()),
            Uid = this.PayloadUid
        }
    }
}

public class UploadedManifestThumbnailDescriptor
{
    public string ThumbnailKey { get; set; }

    public int PixelWidth { get; set; }

    public int PixelHeight { get; set; }

    public string ContentType { get; set; }

    public string CreateTransitKey(string payloadKey)
    {
        //duplicate code in ThumbnailDescriptor
        return
            $"{payloadKey}" +
            $"{DriveFileUtility.TransitThumbnailKeyDelimiter}" +
            $"{this.PixelWidth}" +
            $"{DriveFileUtility.TransitThumbnailKeyDelimiter}" +
            $"{this.PixelHeight}";
    }
}