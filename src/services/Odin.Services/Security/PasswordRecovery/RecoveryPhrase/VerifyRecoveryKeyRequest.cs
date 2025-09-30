using Odin.Services.EncryptionKeyService;

namespace Odin.Services.Security.PasswordRecovery.RecoveryPhrase;

public class VerifyRecoveryKeyRequest
{
    public EccEncryptedPayload EncryptedRecoveryKey { get; set; }
}