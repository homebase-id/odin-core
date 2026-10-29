namespace Odin.Services.Security.PasswordRecovery.RecoveryPhrase;

public class VerifyRecoveryKeyRequest
{
    /// <summary>
    /// The phrase's check value (<c>RecoveryPhraseCheck</c>), worked out in the browser, so the phrase
    /// itself never reaches the server.
    /// </summary>
    public string PhraseCheck64 { get; set; }
}
