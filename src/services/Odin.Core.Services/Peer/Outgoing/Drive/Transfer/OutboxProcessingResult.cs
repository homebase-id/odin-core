using System;
using Odin.Core.Identity;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Peer.Outgoing.Drive.Transfer.Outbox;

namespace Odin.Core.Services.Peer.Outgoing.Drive.Transfer
{
    public class OutboxProcessingResult
    {
        public OdinId Recipient { get; set; }

        /// <summary>
        /// This is the original response code from the host server.
        /// </summary>
        public PeerResponseCode? RecipientPeerResponseCode { get; set; }

        public TransferResult TransferResult { get; set; }

        public InternalDriveFileId File { get; set; }

        public Int64 Timestamp { get; set; }

        public TransitOutboxItem OutboxItem { get; set; }
    }
}