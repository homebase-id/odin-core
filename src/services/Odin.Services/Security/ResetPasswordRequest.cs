using Odin.Core.Cryptography.Login;

namespace Odin.Services.Security;

public class ResetPasswordRequest
{
    public PasswordReply CurrentAuthenticationPasswordReply { get; set; }
    public PasswordReply NewPasswordReply { get; set; }
}