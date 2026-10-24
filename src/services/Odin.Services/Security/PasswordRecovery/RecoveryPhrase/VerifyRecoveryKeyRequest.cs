using Odin.Services.EncryptionKeyService;

namespace Odin.Services.Security.PasswordRecovery.RecoveryPhrase;

public class VerifyRecoveryKeyRequest
{
    /// <summary>The phrase itself, while the server still holds it.</summary>
    public EccEncryptedPayload EncryptedRecoveryKey { get; set; }

    /// <summary>
    /// Once the browser took the phrase over: its check value (<c>RecoveryPhraseCheck</c>), so the
    /// phrase never reaches the server.
    /// </summary>
    public string PhraseCheck64 { get; set; }
}