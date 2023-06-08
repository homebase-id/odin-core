using Odin.Core.Services.Drives;

namespace Odin.Hosting.Controllers.OwnerToken.Transit;

public class TransitQueryBatchRequest : QueryBatchRequest
{
    public string OdinId { get; set; }
}