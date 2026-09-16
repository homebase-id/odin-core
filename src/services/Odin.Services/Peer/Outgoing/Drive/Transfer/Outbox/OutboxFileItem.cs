using System;
using System.Collections.Generic;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Drives;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox
{
    public class OutboxFileItem
    {
        public OdinId Recipient { get; set; }

        public InternalDriveFileId File { get; set; } = new();

        /// <summary>
        /// Specifies how this item should be prioritized by the Outbox Sending
        /// Process.  The lower the number, the higher the priority
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// When the item was added to the outbox (the outbox row's created). The retry-later deadline is
        /// measured from here, so this must be milliseconds: assigning .seconds reads back as 1970.
        /// </summary>
        public UnixTimeUtc AddedTimestamp { get; set; } = UnixTimeUtc.Now();

        public Guid Marker { get; set; }

        public OutboxItemType Type { get; set; }
        public int AttemptCount { get; set; }
        public Guid? DependencyFileId { get; set; }
        public OutboxItemState State { get; set; }
        public string CorrelationId { get; set; }

        public RedactedOutboxFileItem Redacted()
        {
            return new RedactedOutboxFileItem()
            {
                Recipient = Recipient,
                File = File,
                Priority = Priority,
                AddedTimestamp = AddedTimestamp,
                Marker = Marker,
                Type = Type,
                AttemptCount = AttemptCount,
                DependencyFileId = DependencyFileId
            };
        }
    }

    public class RedactedOutboxFileItem
    {
        public OdinId Recipient { get; set; }

        public InternalDriveFileId File { get; set; } = new();

        public int Priority { get; set; }

        public UnixTimeUtc AddedTimestamp { get; set; } = UnixTimeUtc.Now();

        public Guid Marker { get; set; }

        public OutboxItemType Type { get; set; }
        public int AttemptCount { get; set; }
        public Guid? DependencyFileId { get; set; }
    }
}