using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core.Exceptions;
using Odin.Core.Time;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Util;

namespace Odin.Services.Drives.FileSystem.Base.Upload;

/// <summary>
/// Describes the content being uploaded
/// </summary>
public class UploadManifest
{
    public List<UploadManifestPayloadDescriptor> PayloadDescriptors { get; set; } = new();

    public UploadManifestPayloadDescriptor GetPayloadDescriptor(string key)
    {
        return PayloadDescriptors?.SingleOrDefault(p => string.Equals(p.PayloadKey, key, StringComparison.InvariantCultureIgnoreCase));
    }

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

                var anyMissingThumbnailKey = pd.Thumbnails?.Any(thumb => string.IsNullOrEmpty(thumb.ThumbnailKey?.Trim())) ?? false;
                if (anyMissingThumbnailKey)
                {
                    throw new OdinClientException($"The payload key [{pd.PayloadKey}] as a thumbnail missing a thumbnailKey",
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

    /// <summary>
    /// Sets the UIDs for payloads 
    /// </summary>
    public void ResetPayloadUiDs()
    {
        if (this.PayloadDescriptors != null)
        {
            foreach (var pd in this.PayloadDescriptors)
            {
                //These are created in advance to ensure we can
                //upload thumbnails and payloads in any order
                pd.PayloadUid = UnixTimeUtcUnique.Now();
            }
        }
    }
}

/// <summary>
/// Describes the attributes of the payload of a given PayloadKey
/// </summary>
public class UploadManifestPayloadDescriptor
{
    /// <summary>
    /// Indicates how to treat this payload during an update.
    /// </summary>
    public FileUpdateOperationType FileUpdateOperationType { get; init; }

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

    public void AssertIsValid()
    {
        if (this.FileUpdateOperationType == FileUpdateOperationType.AddPayload)
        {
            OdinValidationUtils.AssertNotNull(this.Iv, nameof(Iv));
            OdinValidationUtils.AssertNotEmptyByteArray(this.Iv, nameof(Iv));
            DriveFileUtility.AssertValidPayloadKey(this.PayloadKey);
            OdinValidationUtils.AssertNotNullOrEmpty(ContentType, nameof(ContentType));

            foreach (var thumb in this.Thumbnails ?? [])
            {
                OdinValidationUtils.AssertNotNullOrEmpty(thumb.ThumbnailKey, nameof(thumb.ThumbnailKey));
            }
        }

        if (this.FileUpdateOperationType == FileUpdateOperationType.DeletePayload)
        {
            DriveFileUtility.AssertValidPayloadKey(this.PayloadKey);
        }
    }
}

public class UploadedManifestThumbnailDescriptor
{
    public string ThumbnailKey { get; set; }

    public int PixelWidth { get; set; }

    public int PixelHeight { get; set; }

    public string ContentType { get; set; }
}

public enum FileUpdateOperationType
{
    AddPayload = 2,
    DeletePayload = 3
}