using System;

namespace Youverse.Core.Services.Transit.ReceivingHost;

public class InboxStatus
{
    public int TotalItems { get; set; }
    public int PoppedCount { get; set; }
    public UnixTimeUtc OldestItemTimestamp { get; set; }
    
}