using Odin.Core.Time;

namespace Odin.Services.Authentication.Owner;

public class DecryptedRecoveryKey
{
    public string Key { get; set; }
    public UnixTimeUtc Created { get; set; }
}