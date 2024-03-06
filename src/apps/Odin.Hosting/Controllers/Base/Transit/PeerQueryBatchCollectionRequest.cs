using Odin.Services.Drives;

namespace Odin.Hosting.Controllers.Base.Transit;

public class PeerQueryBatchCollectionRequest : QueryBatchCollectionRequest
{
    public string OdinId { get; set; }
}