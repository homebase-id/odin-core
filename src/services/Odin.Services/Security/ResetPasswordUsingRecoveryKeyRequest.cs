using Odin.Core.Cryptography.Login;
using Odin.Services.EncryptionKeyService;

namespace Odin.Services.Security;

public class ResetPasswordUsingRecoveryKeyRequest
{
    public EccEncryptedPayload EncryptedRecoveryKey { get; set; }
    public PasswordReply PasswordReply { get; set; }
}