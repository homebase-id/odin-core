namespace Odin.Services.Peer.Outgoing.Drive.Reactions;

public class SharedSecretEncryptedTransitPayload
{
    public byte[] Iv { get; init; } = System.Array.Empty<byte>();
    public string Data { get; init; } = "";
}