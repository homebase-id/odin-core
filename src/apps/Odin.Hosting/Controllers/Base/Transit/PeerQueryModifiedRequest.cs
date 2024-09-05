using Odin.Core.Identity;
using Odin.Services.Drives;

namespace Odin.Hosting.Controllers.Base.Transit;

public class PeerQueryModifiedRequest : QueryModifiedRequest
{
    public OdinId OdinId { get; set; }
}