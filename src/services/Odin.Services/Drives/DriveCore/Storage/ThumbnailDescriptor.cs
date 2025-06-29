using System;
using Microsoft.AspNetCore.Http;
using Odin.Core.Exceptions;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Services.Drives.DriveCore.Storage;

public class ThumbnailDescriptor : IEquatable<ThumbnailDescriptor>
{
    public static readonly int MaxThumbnailSize = 1024 * 1024; // 1MB max for thumbnails
    public int PixelWidth { get; set; }

    public int PixelHeight { get; set; }

    /// <summary>
    /// The Mime Type of the thumbnail
    /// </summary>
    public string ContentType { get; set; }

    public uint BytesWritten { get; set; }


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

    public string CreateTransitKey(string payloadKey)
    {
        return
            $"{payloadKey}" +
            $"{TenantPathManager.TransitThumbnailKeyDelimiter}" +
            $"{this.PixelWidth}" +
            $"{TenantPathManager.TransitThumbnailKeyDelimiter}" +
            $"{this.PixelHeight}";
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
        if (BytesWritten > MaxThumbnailSize)
        {
            throw new OdinClientException($"ThumbnailDescriptor BytesWritten {BytesWritten} too long, max {MaxThumbnailSize}");
        }
    }
}