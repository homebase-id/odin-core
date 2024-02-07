using Odin.Core.Services.Drives;

namespace Odin.Hosting.Controllers.Base.Transit;

public class PeerQueryBatchRequest : QueryBatchRequest
{
    public string OdinId { get; set; }
}

public class PeerQueryModifiedRequest : QueryModifiedRequest
{
    public string OdinId { get; set; }
}

public class PeerQueryBatchCollectionRequest : QueryBatchCollectionRequest
{
    public string OdinId { get; set; }
}