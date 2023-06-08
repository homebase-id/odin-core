using System;
using Youverse.Core.Identity;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Transit.SendingHost.Outbox;

namespace Youverse.Core.Services.Transit.SendingHost
{
    public enum Remote
    {
        AcceptedIntoInbox = 2,
        AcceptedDirectWrite = 32
    }
    
    public class SendResult
    {
        public OdinId Recipient { get; set; }
        public bool Success { get; set; }

        /// <summary>
        /// This is the original response code from the host server.
        /// </summary>
        public TransitResponseCode RecipientTransitResponseCode { get; set; }
        
        /// <summary>
        /// Specifies if the file should be put back in the queue
        /// </summary>
        public bool ShouldRetry { get; set; }
        
        public TransferFailureReason? FailureReason { get; set; }

        public InternalDriveFileId File { get; set; }

        public Int64 Timestamp { get; set; }
        
        public TransitOutboxItem OutboxItem { get; set; }
    }
}