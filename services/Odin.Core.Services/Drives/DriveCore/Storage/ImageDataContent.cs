namespace Odin.Core.Services.Drives.DriveCore.Storage;

public class ImageDataContent : ImageDataHeader
{
    /// <summary>
    /// The thumbnail data
    /// </summary>
    public byte[] Content { get; set; }
}