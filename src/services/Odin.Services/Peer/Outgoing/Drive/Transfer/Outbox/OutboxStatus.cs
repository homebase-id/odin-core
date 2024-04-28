using Odin.Core.Time;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

public class OutboxStatus
{
    public int TotalItems { get; set; }
    public int CheckedOutCount { get; set; }
    public UnixTimeUtc NextItemRun { get; set; }
}