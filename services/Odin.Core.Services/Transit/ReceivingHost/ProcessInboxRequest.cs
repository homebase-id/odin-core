using Youverse.Core.Services.Drives;

namespace Youverse.Core.Services.Transit.ReceivingHost;

public class ProcessInboxRequest
{
    public TargetDrive TargetDrive { get; set; }
    
    public int BatchSize { get; set; }
}