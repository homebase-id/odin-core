namespace Odin.Core.Services.Peer.Outgoing;

public class SharedSecretEncryptedTransitPayload
{
    public byte[] Iv { get; set; } = System.Array.Empty<byte>();
    public string Data { get; set; } = "";
}