namespace Youverse.Core.Services.Drive.Core.Storage;

public class ImageDataContent : ImageDataHeader
{
    /// <summary>
    /// The thumbnail data
    /// </summary>
    public byte[] Content { get; set; }
}