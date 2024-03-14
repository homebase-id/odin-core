using System.Collections.Generic;

namespace Odin.Services.Peer.Outgoing.Drive.Reactions;

public class PeerAddReactionRequest
{
    public string OdinId { get; set; }

    public AddRemoteReactionRequest Request { get; set; }
}


public class PeerAddGroupReactionRequest
{
    public List<string> Recipients { get; set; }

    public AddRemoteReactionRequest Request { get; set; }
}

public class PeerDeleteGroupReactionRequest
{
    public List<string> Recipients { get; set; }

    public DeleteReactionRequestByGlobalTransitId Request { get; set; }
}