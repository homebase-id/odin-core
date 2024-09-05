using Odin.Core.Identity;
using Odin.Services.Drives;

namespace Odin.Hosting.Controllers.Base.Transit;

public class PeerQueryBatchCollectionRequest : QueryBatchCollectionRequest
{
    public OdinId OdinId { get; set; }
}