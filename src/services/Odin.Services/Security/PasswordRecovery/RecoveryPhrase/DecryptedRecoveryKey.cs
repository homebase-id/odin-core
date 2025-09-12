using Odin.Core.Time;

namespace Odin.Services.Security.PasswordRecovery.RecoveryPhrase;

public class DecryptedRecoveryKey
{
    public string Key { get; set; }
    public UnixTimeUtc Created { get; set; }
}