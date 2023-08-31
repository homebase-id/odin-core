using Odin.Core;
using Odin.Core.Cryptography.Data;

namespace YouAuthClientReferenceImplementation;

public class State
{
    public string Identity { get; set; } = "";
    public SensitiveByteArray? PrivateKey;
    public EccFullKeyData? KeyPair { get; set; }
}
