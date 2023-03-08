#nullable enable

namespace Youverse.Hosting;

public class SharedSecretEncryptedPayload
{
    public byte[] Iv { get; set; } = System.Array.Empty<byte>();
    public string Data { get; set; } = "";
}