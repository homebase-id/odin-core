using Youverse.Core.Services.Drives;

namespace Youverse.Core.Services.Transit;

public class TransitQueryBatchRequest : QueryBatchRequest
{
    public string OdinId { get; set; }
}