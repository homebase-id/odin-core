using System;
using System.Collections.Generic;
using System.IO;
using Youverse.Core.Identity;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Transit.Outbox
{
    public class OutboxItem
    {
        public OutboxItem()
        {
            this.Id = Guid.NewGuid();
            this.AddedTimestamp = DateTimeExtensions.UnixTimeMilliseconds();
            this.Attempts = new List<TransferAttempt>();
            this.File = new DriveFileId();
        }

        public Guid Id { get; set; }

        /// <summary>
        /// The application that created this outbox item
        /// </summary>
        public string AppId { get; set; }

        /// <summary>
        /// The app client that created this outbox item
        /// </summary>
        public Guid AppClientId { get; set; }
        
        public DotYouIdentity Recipient { get; set; }
        
        public DriveFileId File { get; set; }
        
        /// <summary>
        /// Specifies how this item should be prioritized by the Outbox Sending
        /// Process.  The lower the number, the higher the priority
        /// </summary>
        public int Priority { get; set; }

        public List<TransferAttempt> Attempts { get; }
        
        /// <summary>
        /// Indicates an item is checked out for processing
        /// </summary>
        public bool IsCheckedOut { get; set; }

        public UInt64 AddedTimestamp { get; set; }
    }
}