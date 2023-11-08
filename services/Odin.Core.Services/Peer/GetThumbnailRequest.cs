using System;
using Odin.Core.Services.Drives;

namespace Odin.Core.Services.Peer;

public abstract class GetThumbnailRequestBase
{
    public int Width { get; set; }
    public int Height { get; set; }

    public bool DirectMatchOnly { get; set; } = false;

    public string PayloadKey { get; set; }
}

public class GetThumbnailRequest : GetThumbnailRequestBase
{
    public ExternalFileIdentifier File { get; set; }
}

public class GetThumbnailByGlobalTransitIdRequest : GetThumbnailRequestBase
{
    public GlobalTransitIdFileIdentifier File { get; set; }
}

public class GetThumbnailByUniqueIdRequest : GetThumbnailRequestBase
{
    public Guid ClientUniqueId { get; set; }

    public TargetDrive TargetDrive { get; set; }
}