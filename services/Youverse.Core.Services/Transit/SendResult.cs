using System;
using Youverse.Core.Identity;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Transit.Outbox;

namespace Youverse.Core.Services.Transit
{
    public class SendResult
    {
        public OdinId Recipient { get; set; }
        public bool Success { get; set; }

        /// <summary>
        /// Specifies if the file should be put back in the queue
        /// </summary>
        public bool ShouldRetry { get; set; }
        
        public TransferFailureReason? FailureReason { get; set; }

        public InternalDriveFileId File { get; set; }

        public UInt64 Timestamp { get; set; }
        
        public TransitOutboxItem OutboxItem { get; set; }
    }
}