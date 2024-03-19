using Odin.Core.Identity;
using Odin.Services.Peer.Outgoing.Drive.Reactions;

namespace Odin.Services.Drives.Reactions;


public class AddDeleteRemoteReactionResponse
{
    public OdinId Recipient { get; set; }
    public AddDeleteRemoteReactionStatusCode Status { get; set; }
}