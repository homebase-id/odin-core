using System;

namespace Odin.Core.Services.Peer.SendingHost
{
    public class TransferAttempt
    {
        public Int64 Timestamp { get; set; }
        public TransferFailureReason TransferFailureReason { get; set; }
    }
}