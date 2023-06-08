using Youverse.Core.Services.Drives;

namespace Youverse.Hosting.Controllers.OwnerToken.Transit;

public class TransitQueryBatchRequest : QueryBatchRequest
{
    public string OdinId { get; set; }
}