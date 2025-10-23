using Odin.Core.Time;

namespace Odin.Services.Security.PasswordRecovery.RecoveryPhrase;

public class RecoveryKeyResult
{
    public string Key { get; set; }
    public UnixTimeUtc Created { get; set; }
    public UnixTimeUtc? NextViewableDate { get; set; }
    public bool HasInitiallyReviewedKey { get; set; }
}