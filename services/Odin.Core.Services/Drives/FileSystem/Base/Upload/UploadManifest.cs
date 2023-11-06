using System.Collections.Generic;
using Odin.Core.Services.Drives.DriveCore.Storage;

namespace Odin.Core.Services.Drives.FileSystem.Base.Upload;

/// <summary>
/// Describes the content being uploaded
/// </summary>
public class UploadManifest
{
    public UploadManifest()
    {
        PayloadDescriptors = new List<UploadManifestPayloadDescriptor>();
    }
    public List<UploadManifestPayloadDescriptor> PayloadDescriptors { get; set; }
}

/// <summary>
/// Describes the attributes of the payload of a given PayloadKey
/// </summary>
public class UploadManifestPayloadDescriptor
{
    public string PayloadKey { get; set; }

    /// <summary>
    /// The thumbnails expected for this payload key
    /// </summary>
    public IEnumerable<UploadedManifestThumbnailDescriptor> Thumbnails { get; set; }

    //other stuff when needed
}

public class UploadedManifestThumbnailDescriptor
{
    public string ThumbnailKey { get; set; }
    public int PixelWidth { get; set; }

    public int PixelHeight { get; set; }
}