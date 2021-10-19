using System;
using System.Collections.Generic;

namespace Youverse.Core.Services.Transit
{
    public class TransferResult
    {
        public TransferResult(Guid transferId)
        {
            this.TransferId = transferId;
        }

        public Guid TransferId { get; set; }
        public List<string> SuccessfulRecipients { get; set; } = new List<string>();
        public List<string> QueuedRecipients { get; set; } = new List<string>();
    }
}