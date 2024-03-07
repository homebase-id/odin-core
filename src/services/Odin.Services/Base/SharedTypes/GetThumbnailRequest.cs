using Odin.Services.Drives;

namespace Odin.Services.Base.SharedTypes;

public class GetThumbnailRequest : GetThumbnailRequestBase
{
    public ExternalFileIdentifier File { get; set; }
}