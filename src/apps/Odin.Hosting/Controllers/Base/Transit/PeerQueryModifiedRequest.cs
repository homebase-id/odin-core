using Odin.Services.Drives;

namespace Odin.Hosting.Controllers.Base.Transit;

public class PeerQueryModifiedRequest : QueryModifiedRequest
{
    public string OdinId { get; set; }
}