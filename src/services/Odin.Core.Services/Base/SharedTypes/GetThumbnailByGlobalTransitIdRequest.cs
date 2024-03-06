using Odin.Core.Services.Drives;

namespace Odin.Core.Services.Base.SharedTypes;

public class GetThumbnailByGlobalTransitIdRequest : GetThumbnailRequestBase
{
    public GlobalTransitIdFileIdentifier File { get; set; }
}