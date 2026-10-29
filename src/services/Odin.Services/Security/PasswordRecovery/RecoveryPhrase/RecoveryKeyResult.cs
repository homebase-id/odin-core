using Odin.Core.Time;

namespace Odin.Services.Security.PasswordRecovery.RecoveryPhrase;

public class RecoveryKeyResult
{
    /// <summary>The 12 words, while the server still holds the phrase.  Null once the browser took it over.</summary>
    public string Key { get; set; }

    /// <summary>
    /// Once the browser took the phrase over: the phrase locked by the zero-knowledge master key, for
    /// the browser to open and show.  Released on the same schedule as <see cref="Key"/>.
    /// </summary>
    public string ZkMasterKeyEncryptedPhraseKey { get; set; }

    public UnixTimeUtc Created { get; set; }
    public UnixTimeUtc? NextViewableDate { get; set; }
    public bool HasInitiallyReviewedKey { get; set; }
}