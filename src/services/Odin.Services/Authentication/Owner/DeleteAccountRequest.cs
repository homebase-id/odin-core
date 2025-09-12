using Odin.Core.Cryptography.Login;

namespace Odin.Services.Authentication.Owner;

public class DeleteAccountRequest
{
    public PasswordReply CurrentAuthenticationPasswordReply { get; set; }
}