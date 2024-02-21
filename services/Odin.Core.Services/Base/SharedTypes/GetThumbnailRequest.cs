using Odin.Core.Services.Drives;

namespace Odin.Core.Services.Base.SharedTypes;

public class GetThumbnailRequest : GetThumbnailRequestBase
{
    public ExternalFileIdentifier File { get; set; }
}