using System.Collections.Generic;

namespace Odin.Services.Peer.Outgoing.Drive.Reactions;

public class AddGroupReactionResponse
{
    public List<RemoteAddDeleteReactionResponse> Responses { get; set; } = new ();
}

public class DeleteGroupReactionResponse
{
    public List<RemoteAddDeleteReactionResponse> Responses { get; set; } = new ();
}