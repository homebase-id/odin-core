using Odin.Core.Cryptography.Login;

namespace Odin.Hosting.Controllers.OwnerToken.Security;

/// <summary>
/// A server-made deal's recovery: the secret the recovering browser rebuilt from the bundle, and the
/// new password.
/// </summary>
public class FinalRecoveryRequest
{
    public string Secret64 { get; init; }
    public PasswordReply PasswordReply { get; set; }
}

/// <summary>The link from an automatic holder's email: its shard and the code it carries.</summary>
public class FillSlotRequest
{
    public string ShardId { get; init; }
    public string Code { get; init; }
}
