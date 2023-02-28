using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Transit;

public class TransitQueryBatchRequest : QueryBatchRequest
{
    public string OdinId { get; set; }
}