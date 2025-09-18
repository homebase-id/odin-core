using Odin.Core.Cryptography.Login;

namespace Odin.Hosting.Controllers.OwnerToken.Security;

public class UpdateRecoveryEmailRequest
{
    public string Email { get; init; }
    public PasswordReply PasswordReply { get; init; }
}