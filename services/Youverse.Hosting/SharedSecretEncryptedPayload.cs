#nullable enable

namespace Youverse.Hosting;

public class SharedSecretEncryptedPayload
{
    public byte[] Iv { get; set; }
    public string Data { get; set; }
}