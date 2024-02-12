namespace Odin.Core.Services.Peer.Outgoing.Drive.Reactions;

public class TransitGetReactionsRequest
{
    public string OdinId { get; set; }

    public GetRemoteReactionsRequest Request { get; set; }
}