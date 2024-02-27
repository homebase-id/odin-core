using Odin.Core.Cryptography.Login;
using Odin.Core.Services.EncryptionKeyService;
using Odin.Core.Time;

namespace Odin.Core.Services.Authentication.Owner;

public class ResetPasswordUsingRecoveryKeyRequest
{
    public RsaEncryptedPayload EncryptedRecoveryKey { get; set; }
    public PasswordReply PasswordReply { get; set; }
}

public class ResetPasswordRequest
{
    public PasswordReply CurrentAuthenticationPasswordReply { get; set; }
    public PasswordReply NewPasswordReply { get; set; }
}

public class DeleteAccountRequest
{
    public PasswordReply CurrentAuthenticationPasswordReply { get; set; }
}

public class DeleteAccountResponse
{
    public UnixTimeUtc PlannedDeletionDate { get; set; }
}

public class AccountStatusResponse
{
    public UnixTimeUtc? PlannedDeletionDate { get; set; }
    public string PlanId { get; set; }
}