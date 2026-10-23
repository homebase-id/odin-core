using Odin.Core.Cryptography.Login;
using Odin.Services.Security.ZeroKnowledge;

namespace Odin.Services.Security;

public class ResetPasswordRequest
{
    public PasswordReply CurrentAuthenticationPasswordReply { get; set; }
    public PasswordReply NewPasswordReply { get; set; }

    /// <summary>
    /// The zero-knowledge keys re-locked with the new password.  Required once the owner has them:
    /// without it they would stay locked by the old password.
    /// </summary>
    public ZkKeysRequest ZkKeys { get; set; }
}
