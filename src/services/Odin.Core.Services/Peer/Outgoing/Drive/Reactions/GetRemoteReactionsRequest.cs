using Odin.Core.Services.Drives;

namespace Odin.Core.Services.Peer.Outgoing.Drive.Reactions;

public class GetRemoteReactionsRequest
{
    public GlobalTransitIdFileIdentifier File { get; set; }
    public int Cursor { get; set; }
    public int MaxRecords { get; set; }
}