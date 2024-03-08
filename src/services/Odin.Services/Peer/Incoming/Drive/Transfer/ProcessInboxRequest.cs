using Odin.Services.Drives;

namespace Odin.Services.Peer.Incoming.Drive.Transfer;

public class ProcessInboxRequest
{
    public TargetDrive TargetDrive { get; set; }
    
    public int BatchSize { get; set; }
}