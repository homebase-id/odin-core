using System;
using System.Collections;
using System.Collections.Generic;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Transit
{
    public class OutboxQueueItem
    {
        public OutboxQueueItem()
        {
            this.Id = Guid.NewGuid();
            this.Attempts = new List<TransferAttempt>();
        }
        
        public Guid Id { get; set; }
        public DotYouIdentity Recipient { get; set; }
        public Guid FileId { get; set; }

        public List<TransferAttempt> Attempts { get; }
    }
}