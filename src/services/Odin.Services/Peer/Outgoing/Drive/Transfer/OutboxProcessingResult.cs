using System;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Drives;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer
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

        public UnixTimeUtc Timestamp { get; set; }

        public OutboxFileItem OutboxFileItem { get; set; }
        public Guid? VersionTag { get; set; }
    }
}