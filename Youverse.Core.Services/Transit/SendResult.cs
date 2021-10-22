using System;

namespace Youverse.Core.Services.Transit
{
    public class SendResult
    {
        public string Recipient { get; set; }
        public bool Success { get; set; }

        public TransferFailureReason? FailureReason { get; set; }

        public TransferEnvelope TransferEnvelope { get; set; }

        public Int64 Timestamp { get; set; }
    }
}