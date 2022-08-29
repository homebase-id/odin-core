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
            this.AddedTimestamp = DateTimeExtensions.UnixTimeMilliseconds();
            this.Attempts = new List<TransferAttempt>();
            this.File = new InternalDriveFileId();
        }
        
        public DotYouIdentity Recipient { get; set; }
        
        public InternalDriveFileId File { get; set; }
        
        /// <summary>
        /// Specifies how this item should be prioritized by the Outbox Sending
        /// Process.  The lower the number, the higher the priority
        /// </summary>
        public int Priority { get; set; }

        public List<TransferAttempt> Attempts { get; }

        public UInt64 AddedTimestamp { get; set; }

        public byte[] Marker { get; set; }
    }
}