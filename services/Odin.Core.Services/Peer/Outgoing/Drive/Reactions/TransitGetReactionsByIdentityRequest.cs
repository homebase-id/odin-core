using Odin.Core.Identity;
using Odin.Core.Services.Drives;

namespace Odin.Core.Services.Peer.Outgoing.Drive.Reactions;

public class TransitGetReactionsByIdentityRequest
{
    /// <summary>
    /// The remote identity server 
    /// </summary>
    public OdinId OdinId { get; set; }

    public OdinId Identity { get; set; }

    public GlobalTransitIdFileIdentifier File { get; set; }
}