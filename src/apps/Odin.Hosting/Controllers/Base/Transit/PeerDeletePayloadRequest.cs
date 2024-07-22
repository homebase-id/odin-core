using Odin.Hosting.Controllers.Base.Drive;

namespace Odin.Hosting.Controllers.Base.Transit;

public class PeerDeletePayloadRequest: DeletePayloadRequest
{
    public string OdinId { get; set; }
}