using Odin.Core.Services.Drives;

namespace Odin.Core.Services.Peer;

public class GetThumbnailRequest
{
    public ExternalFileIdentifier File { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public bool DirectMatchOnly { get; set; } = false;
    
    public string PayloadKey { get; set; }
}