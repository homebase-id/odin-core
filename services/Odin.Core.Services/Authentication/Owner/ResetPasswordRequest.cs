using Odin.Core.Cryptography;
using Odin.Core.Services.EncryptionKeyService;

namespace Odin.Core.Services.Authentication.Owner;

public class ResetPasswordRequest
{
    public RsaEncryptedPayload EncryptedRecoveryKey { get; set; }
    public PasswordReply PasswordReply { get; set; }
}