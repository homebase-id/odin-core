using Odin.Core.Cryptography.Login;

namespace Odin.Hosting.Controllers.OwnerToken.Security;

public class FinalRecoveryRequest
{
    public string Id { get; init; }
    public string FinalKey { get; init; }
    public PasswordReply PasswordReply { get; set; }
}