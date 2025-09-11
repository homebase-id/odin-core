using Odin.Core.Identity;

namespace Odin.Services.Security;

public class PlayerShardHealthResult
{
    public OdinId PlayerId { get; set; }        // or string if odinId is not a Guid
    public bool IsValid { get; set; }
}