using Odin.Core.Cryptography;
using Odin.Core.Services.EncryptionKeyService;

namespace Odin.Core.Services.Authentication.Owner;

public class ResetPasswordUsingRecoveryKeyRequest
{
    public RsaEncryptedPayload EncryptedRecoveryKey { get; set; }
    public PasswordReply PasswordReply { get; set; }
}

public class ResetPasswordRequest
{
    public PasswordReply CurrentPasswordReply { get; set; }
    public PasswordReply NewPasswordReply { get; set; }
}