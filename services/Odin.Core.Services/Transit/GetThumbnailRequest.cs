using Odin.Core.Services.Drives;

namespace Odin.Core.Services.Transit;

public class GetThumbnailRequest
{
    public ExternalFileIdentifier File { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public bool DirectMatchOnly { get; set; } = false;
}