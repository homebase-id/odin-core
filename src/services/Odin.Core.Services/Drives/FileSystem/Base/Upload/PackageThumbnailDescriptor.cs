namespace Odin.Core.Services.Drives.FileSystem.Base.Upload;

/// <summary>
/// Temporary storage class during upload process
/// </summary>
public class PackageThumbnailDescriptor
{
    public int PixelWidth { get; set; }

    public int PixelHeight { get; set; }

    public string ContentType { get; set; }

    public string PayloadKey { get; set; }
}