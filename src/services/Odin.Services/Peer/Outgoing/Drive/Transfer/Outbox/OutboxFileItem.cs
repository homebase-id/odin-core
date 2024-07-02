using System;
using System.Collections.Generic;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Drives;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox
{
    public class OutboxFileItem
    {
        public OutboxFileItem()
        {
            this.AddedTimestamp = UnixTimeUtc.Now().seconds;
            this.Attempts = new List<TransferAttempt>();
            this.File = new InternalDriveFileId();
        }

        public OdinId Recipient { get; set; }

        public InternalDriveFileId File { get; set; }

        /// <summary>
        /// Specifies how this item should be prioritized by the Outbox Sending
        /// Process.  The lower the number, the higher the priority
        /// </summary>
        public int Priority { get; set; }

        public List<TransferAttempt> Attempts { get; }

        public Int64 AddedTimestamp { get; set; }

        public Guid Marker { get; set; }

        public OutboxItemType Type { get; set; }
        public int AttemptCount { get; set; }
        public Guid? DependencyFileId { get; set; }
        public OutboxItemState State { get; set; }
    }
}