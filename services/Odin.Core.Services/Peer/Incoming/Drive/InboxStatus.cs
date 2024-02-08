using Odin.Core.Time;

namespace Odin.Core.Services.Peer.Incoming.Drive;

public class InboxStatus
{
    public int TotalItems { get; set; }
    public int PoppedCount { get; set; }
    public UnixTimeUtc OldestItemTimestamp { get; set; }
}

