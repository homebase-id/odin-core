using Odin.Core.Exceptions;
using System;

namespace Odin.Services.Drives.DriveCore.Storage;

public class ThumbnailContent : ThumbnailDescriptor
{
    public static readonly int MaxTinyThumbLength = 1024;

    /// <summary>
    /// The thumbnail data
    /// </summary>
    public byte[] Content { get; set; }

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
        if (Content != null)
        {
            if (Content.Length > MaxTinyThumbLength)
                throw new OdinClientException($"Thumbnail size of {Content.Length} exceeds {MaxTinyThumbLength}");
        }
    }
}