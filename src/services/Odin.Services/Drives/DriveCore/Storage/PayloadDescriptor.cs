using System;
using System.Collections.Generic;
using Odin.Core.Exceptions;
using Odin.Core.Time;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Util;

namespace Odin.Services.Drives.DriveCore.Storage;

/// <summary>
/// Defines a payload
/// </summary>
public class PayloadDescriptor
{
    public static readonly int MaxDescriptorContentLength = 1024;
    public static readonly int MaxThumbnailsCount = 5;


    public PayloadDescriptor()
    {
        this.Thumbnails = new List<ThumbnailDescriptor>();
    }

    /// <summary>
    /// Initialization vector for the encrypted payload
    /// </summary>
    public byte[] Iv { get; set; }

    /// <summary>
    /// A text value specified by the app to define the payload
    /// </summary>
    public string Key { get; set; }

    public string ContentType { get; set; }

    public long BytesWritten { get; set; }

    public UnixTimeUtc LastModified { get; set; }

    /// <summary>
    /// Content describing this payload (in what ever format you want)
    /// </summary>
    public string DescriptorContent { get; set; }

    public ThumbnailContent PreviewThumbnail { get; set; }

    /// <summary>
    /// Set of thumbnails for this payload in addition to the Appdata.PreviewThumbnail
    /// </summary>
    public List<ThumbnailDescriptor> Thumbnails { get; set; }

    /// <summary>
    /// A sequential guid used for each instance of this payload.This is used as part of storage
    /// and changes each time you upload a new payload with this key
    /// </summary>
    public UnixTimeUtcUnique Uid { get; set; }

    public string GetLastModifiedHttpHeaderValue()
    {
        return LastModified.ToDateTime().ToString("R");
    }

    public bool IsValid()
    {
        var hasValidContentType = !(string.IsNullOrEmpty(ContentType) || string.IsNullOrWhiteSpace(ContentType));
        var hasValidKey = DriveFileUtility.IsValidPayloadKey(Key);
        return hasValidKey && hasValidContentType;
    }

    public bool TryValidate()
    {
        try
        {
            Validate();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Validate()
    {
        if (DescriptorContent?.Length > MaxDescriptorContentLength)
            throw new OdinClientException($"Too long DescriptorContent length {DescriptorContent.Length} in PayloadDescriptor max {MaxDescriptorContentLength}");

        PreviewThumbnail?.Validate();

        if (Thumbnails != null)
        {
            if (Thumbnails?.Count > MaxThumbnailsCount)
                throw new OdinClientException($"Too many Thumbnails count {Thumbnails.Count} in PayloadDescriptor max {MaxThumbnailsCount}");

            foreach (var thumbnail in Thumbnails)
                thumbnail.Validate();
        }
    }

}