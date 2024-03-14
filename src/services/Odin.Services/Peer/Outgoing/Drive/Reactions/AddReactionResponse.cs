using Odin.Core.Identity;

namespace Odin.Services.Peer.Outgoing.Drive.Reactions;

public enum AddDeleteReactionStatusCode
{
    Failure = 0,
    Success = 1
}
public class RemoteAddDeleteReactionResponse
{
    public OdinId Recipient { get; set; }
    public AddDeleteReactionStatusCode Status { get; set; }
}