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
            this.AddedTimestamp = UnixTimeUtcSeconds.Now().seconds;
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

        /// <summary>
        /// Indicates the file should be read from the temp folder of the drive and deleted after it is sent to all recipients
        /// </summary>
        public bool IsTransientFile { get; set; }
    }
}