using System;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Transit
{
    public class SendResult
    {
        public DotYouIdentity Recipient { get; set; }
        public bool Success { get; set; }

        public TransferFailureReason? FailureReason { get; set; }

        public Guid FileId { get; set; }

        public UInt64 Timestamp { get; set; }
        public Guid OutboxItemId { get; set; }
    }
}