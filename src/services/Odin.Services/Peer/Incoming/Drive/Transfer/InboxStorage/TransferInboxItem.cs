using System;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Services.Peer.Outgoing;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Membership.Connections.Requests;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage
{
    public class TransferInboxItem
    {
        public TransferInboxItem()
        {
            this.Id = Guid.NewGuid();
        }

        public Guid Id { get; set; }

        public TransferInstructionType InstructionType { get; set; }

        public Guid GlobalTransitId { get; set; }

        public Guid FileId { get; set; }

        public Guid DriveId { get; set; }

        public UnixTimeUtc AddedTimestamp { get; set; }

        public OdinId Sender { get; set; }

        /// <summary>
        /// Specifies how this item should be prioritized by the Outbox Sending
        /// Process.  The lower the number, the higher the priority
        /// </summary>
        public int Priority { get; set; }

        public Guid Marker { get; set; }

        public string CorrelationId { get; set; }

        /// <summary>
        /// The FileSystemType of the incoming file
        /// </summary>
        public FileSystemType FileSystemType { get; set; }

        public TransferFileType TransferFileType { get; set; }

        public EncryptedKeyHeader SharedSecretEncryptedKeyHeader { get; set; }

        public EncryptedRecipientTransferInstructionSet TransferInstructionSet { get; set; }

        /// <summary>
        /// The incoming file's metadata, carried on the inbox row itself.
        /// </summary>
        /// <remarks>
        /// Historically the incoming <see cref="FileMetadata"/> was staged as a <c>.metadata</c> file in the
        /// per-drive inbox folder and re-read from disk during inbox processing. We now persist it here (the whole
        /// item is serialized into the inbox row's <c>value</c> blob), which removes the need for the inbox folder
        /// on disk. <c>null</c> means the item was queued by an older build that still staged the metadata on disk;
        /// processing falls back to reading the <c>.metadata</c> file for those (dual-read transition).
        /// </remarks>
        // TODO:INBOX The property stays, but once the inbox folder is drained the null case is impossible:
        // drop the "<c>null</c> means legacy" handling and treat this as always-present.
        public FileMetadata FileMetadata { get; set; }

        //Feed bolt-ons
        public EccEncryptedPayload EncryptedFeedPayload { get; set; }
        
        /// <summary>
        /// Serialized data specific to this inbox item
        /// </summary>
        // public string Data { get; set; }

        /// <summary>
        /// Generic field to hold serialized information for the given <see cref="InstructionType"/>
        /// </summary>
        public byte[] Data { get; init; }
    }
}