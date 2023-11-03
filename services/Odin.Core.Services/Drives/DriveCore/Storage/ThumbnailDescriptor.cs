using System;
using Odin.Core.Time;

namespace Odin.Core.Services.Drives.DriveCore.Storage;

public class ThumbnailDescriptor : IEquatable<ThumbnailDescriptor>
{
    public int PixelWidth { get; set; }

    public int PixelHeight { get; set; }

    /// <summary>
    /// The Mime Type of the thumbnail
    /// </summary>
    public string ContentType { get; set; }

    public string GetFilename(string payloadKey)
    {
        return $"{this.PixelWidth}x{this.PixelHeight}-{payloadKey}";
    }
    
    public bool Equals(ThumbnailDescriptor other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return PixelWidth == other.PixelWidth && PixelHeight == other.PixelHeight && ContentType?.ToLower() == other.ContentType?.ToLower();
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((ThumbnailDescriptor)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(PixelWidth, PixelHeight, ContentType);
    }
}