using System;
using System.Collections.Generic;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Transit.Outbox
{
    public class OutboxItem
    {
        public OutboxItem()
        {
            this.Id = Guid.NewGuid();
            this.Attempts = new List<TransferAttempt>();
        }

        public Guid Id { get; set; }

        /// <summary>
        /// The application that created this outbox item
        /// </summary>
        public string AppId { get; set; }

        /// <summary>
        /// The device that created this outbox item
        /// </summary>
        public string DeviceUid { get; set; }
        
        public DotYouIdentity Recipient { get; set; }

        public Guid FileId { get; set; }
        
        /// <summary>
        /// Specifies how this item should be prioritized by the Outbox Sending
        /// Process.  The lower the number, the higher the priority
        /// </summary>
        public int Priority { get; set; }

        public List<TransferAttempt> Attempts { get; }
    }
}