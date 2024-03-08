using Odin.Core.Identity;
using Odin.Services.Drives;

namespace Odin.Services.Peer.Outgoing.Drive.Reactions;

public class TransitGetReactionsByIdentityRequest
{
    /// <summary>
    /// The remote identity server 
    /// </summary>
    public OdinId OdinId { get; set; }

    public OdinId Identity { get; set; }

    public GlobalTransitIdFileIdentifier File { get; set; }
}