using Odin.Core.Time;

namespace Odin.Services.Peer.Incoming.Drive.Transfer;

public class InboxStatus
{
    public int TotalItems { get; set; }
    public int PoppedCount { get; set; }
    public UnixTimeUtc OldestItemTimestamp { get; set; }
}

