using Odin.Core.Identity;
using Odin.Services.Drives;

namespace Odin.Hosting.Controllers.Base.Transit;

public class PeerQueryBatchRequest : QueryBatchRequest
{
    public OdinId OdinId { get; set; }
}