using Odin.Services.Drives;

namespace Odin.Hosting.Controllers.Base.Transit;

public class PeerQueryBatchRequest : QueryBatchRequest
{
    public string OdinId { get; set; }
}