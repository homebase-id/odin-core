using System;
using System.Collections.Generic;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Transit.Inbox
{
    public class InboxItem
    {
        public InboxItem()
        {
            this.Id = Guid.NewGuid();
        }

        public Guid Id { get; set; }

        public UInt64 AddedTimestamp { get; set; }
        
        /// <summary>
        /// The application that created this outbox item
        /// </summary>
        public string AppId { get; set; }

        public DotYouIdentity Sender { get; set; }

        public Guid FileId { get; set; }

        /// <summary>
        /// The Id used to track the reception of this inbox item
        /// </summary>
        public Guid TrackerId { get; set; }

        /// <summary>
        /// Specifies how this item should be prioritized by the Outbox Sending
        /// Process.  The lower the number, the higher the priority
        /// </summary>
        public int Priority { get; set; }

    }
}