namespace Youverse.Core.Services.Drive.Storage;

public class ThumbnailContent : ThumbnailHeader
{
    /// <summary>
    /// The thumbnail data
    /// </summary>
    public byte[] Content { get; set; }
}