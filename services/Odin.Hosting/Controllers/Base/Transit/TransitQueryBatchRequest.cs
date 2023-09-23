using Odin.Core.Services.Drives;

namespace Odin.Hosting.Controllers.Base.Transit;

public class TransitQueryBatchRequest : QueryBatchRequest
{
    public string OdinId { get; set; }
}

public class TransitQueryModifiedRequest : QueryModifiedRequest
{
    public string OdinId { get; set; }
}

public class TransitQueryBatchCollectionRequest : QueryBatchCollectionRequest
{
    public string OdinId { get; set; }
}