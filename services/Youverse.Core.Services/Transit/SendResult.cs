using System;
using Youverse.Core.Identity;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit.Outbox;

namespace Youverse.Core.Services.Transit
{
    public class SendResult
    {
        public DotYouIdentity Recipient { get; set; }
        public bool Success { get; set; }

        public TransferFailureReason? FailureReason { get; set; }

        public InternalDriveFileId File { get; set; }

        public UInt64 Timestamp { get; set; }
        
        public OutboxItem OutboxItem { get; set; }
    }
}