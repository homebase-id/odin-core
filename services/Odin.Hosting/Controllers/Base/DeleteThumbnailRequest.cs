using Odin.Core.Services.Drives;

namespace Odin.Hosting.Controllers.Base;

public class DeletePayloadRequest
{
    public string Key { get; set; }

    public ExternalFileIdentifier File { get; set; }
}

public class DeleteThumbnailRequest
{

    public ExternalFileIdentifier File { get; set; }
    
    public int Width { get; set; }
    
    public int Height { get; set; }
}