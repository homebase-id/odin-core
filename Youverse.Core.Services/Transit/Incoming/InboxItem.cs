using System;
using Youverse.Core.Identity;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Transit.Incoming
{
    public class InboxItem
    {
        public InboxItem()
        {
            this.Id = Guid.NewGuid();
        }

        public Guid Id { get; set; }

        public ExternalFileIdentifier File { get; set; }
        
        public UInt64 AddedTimestamp { get; set; }
        
        public DotYouIdentity Sender { get; set; }
        
        /// <summary>
        /// Specifies how this item should be prioritized by the Outbox Sending
        /// Process.  The lower the number, the higher the priority
        /// </summary>
        public int Priority { get; set; }
    }
}