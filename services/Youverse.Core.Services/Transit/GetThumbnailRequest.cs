using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Transit;

public class GetThumbnailRequest
{
    public ExternalFileIdentifier File { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}