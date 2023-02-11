namespace Youverse.Core.Services.Drive.Core.Storage;

public class ImageDataHeader
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
}