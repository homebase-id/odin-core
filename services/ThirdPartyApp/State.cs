using Odin.Core;
using Odin.Core.Cryptography.Data;

namespace ThirdPartyApp;

public class State
{
    public string Identity { get; set; } = "";
    public SensitiveByteArray? PrivateKey;
    public EccFullKeyData? KeyPair { get; set; }
}
