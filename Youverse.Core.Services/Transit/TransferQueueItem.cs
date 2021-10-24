using System;
using System.Collections.Generic;

namespace Youverse.Core.Services.Transit
{
    public class TransferQueueItem
    {
        public TransferQueueItem()
        {
            this.Id = Guid.NewGuid();
            this.Attempts = new List<TransferAttempt>();
        }
        
        public Guid Id { get; set; }
        public string Recipient { get; set; }
        public EncryptedFile EncryptedFile { get; set; }

        public List<TransferAttempt> Attempts { get; }
    }
}