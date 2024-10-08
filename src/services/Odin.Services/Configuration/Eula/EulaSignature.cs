using Odin.Core.Time;

namespace Odin.Services.Configuration.Eula;

public class EulaSignature
{
    public UnixTimeUtc SignatureDate { get; set; }
    public string Version { get; set; }

    public byte[] SignatureBytes { get; set; }
}