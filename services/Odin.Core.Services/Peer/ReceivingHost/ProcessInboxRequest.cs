using Odin.Core.Services.Drives;

namespace Odin.Core.Services.Peer.ReceivingHost;

public class ProcessInboxRequest
{
    public TargetDrive TargetDrive { get; set; }
    
    public int BatchSize { get; set; }
}