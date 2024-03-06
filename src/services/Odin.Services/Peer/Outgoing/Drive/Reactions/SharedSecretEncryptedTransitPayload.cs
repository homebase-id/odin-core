namespace Odin.Services.Peer.Outgoing.Drive.Reactions;

public class SharedSecretEncryptedTransitPayload
{
    public byte[] Iv { get; set; } = System.Array.Empty<byte>();
    public string Data { get; set; } = "";
}