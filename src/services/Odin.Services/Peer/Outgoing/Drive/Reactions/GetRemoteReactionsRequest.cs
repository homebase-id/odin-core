using Odin.Services.Drives;

namespace Odin.Services.Peer.Outgoing.Drive.Reactions;

public class GetRemoteReactionsRequest
{
    public GlobalTransitIdFileIdentifier File { get; set; }
    public string Cursor { get; set; }
    public int MaxRecords { get; set; }
}