using System;

namespace Odin.Core.Services.Drives.DriveCore.Storage;

public class ImageDataHeader: IEquatable<ImageDataHeader>
{
    public int PixelWidth { get; set; }

    public int PixelHeight { get; set; }

    /// <summary>
    /// The Mime Type of the thumbnail
    /// </summary>
    public string ContentType { get; set; }

    public string GetFilename()
    {
        return $"{this.PixelWidth}x{this.PixelHeight}";
    }

    public bool Equals(ImageDataHeader other)
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
        return Equals((ImageDataHeader)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(PixelWidth, PixelHeight, ContentType);
    }
}