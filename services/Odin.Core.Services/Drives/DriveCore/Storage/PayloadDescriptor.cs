using System;
using System.Collections.Generic;
using Odin.Core.Time;

namespace Odin.Core.Services.Drives.DriveCore.Storage;

/// <summary>
/// Defines a payload
/// </summary>
public class PayloadDescriptor
{
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
    public Guid Uid { get; set; }
    
    public string GetLastModifiedHttpHeaderValue()
    {
        return LastModified.ToDateTime().ToString("R");
    }
    
    public bool IsValid()
    {
        var hasValidContentType = !(string.IsNullOrEmpty(ContentType) || string.IsNullOrWhiteSpace(ContentType));
        var hasValidKey = !(string.IsNullOrEmpty(Key) || string.IsNullOrWhiteSpace(Key));
        return hasValidKey && hasValidContentType;
    }
}

