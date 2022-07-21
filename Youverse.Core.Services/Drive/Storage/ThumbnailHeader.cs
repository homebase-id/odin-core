namespace Youverse.Core.Services.Drive.Storage;

public class ThumbnailHeader
{
    public int PixelWidth { get; set; }

    public int PixelHeight { get; set; }

    /// <summary>
    /// The Mime Type of the thumbnail
    /// </summary>
    public string ContentType { get; set; }
}