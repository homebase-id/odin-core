using System;
using System.Collections.Generic;
using System.Diagnostics;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Drives;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox
{
    [DebuggerDisplay("{Type} to {Recipient}")]
    public class OutboxFileItem
    {
        public OdinId Recipient { get; set; }

        public InternalDriveFileId File { get; set; } = new();

        /// <summary>
        /// Specifies how this item should be prioritized by the Outbox Sending
        /// Process.  The lower the number, the higher the priority
        /// </summary>
        public int Priority { get; set; }
        
        public Int64 AddedTimestamp { get; set; } = UnixTimeUtc.Now().seconds;

        public Guid Marker { get; set; }

        public OutboxItemType Type { get; set; }
        public int AttemptCount { get; set; }
        public Guid? DependencyFileId { get; set; }
        public OutboxItemState State { get; set; }
    }
}