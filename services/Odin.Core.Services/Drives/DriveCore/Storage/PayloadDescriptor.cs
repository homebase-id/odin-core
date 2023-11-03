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
    /// A text value specified by the app to define the payload
    /// </summary>
    public string Key { get; set; }

    public string ContentType { get; set; }

    public long BytesWritten { get; set; }
    
    public UnixTimeUtc LastModified { get; set; }

    /// <summary>
    /// Set of thumbnails for this payload in addition to the Appdata.PreviewThumbnail
    /// </summary>
    public List<ThumbnailDescriptor> Thumbnails { get; set; }
    
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

