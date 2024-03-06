using Odin.Services.Drives;

namespace Odin.Services.Base.SharedTypes;

public class GetThumbnailByGlobalTransitIdRequest : GetThumbnailRequestBase
{
    public GlobalTransitIdFileIdentifier File { get; set; }
}