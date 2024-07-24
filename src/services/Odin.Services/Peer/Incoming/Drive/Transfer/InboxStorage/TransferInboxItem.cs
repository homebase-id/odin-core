using System;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Services.Peer.Outgoing;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

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

        /// <summary>
        /// The FileSystemType of the incoming file
        /// </summary>
        public FileSystemType FileSystemType { get; set; }

        public TransferFileType TransferFileType { get; set; }

        public EncryptedKeyHeader SharedSecretEncryptedKeyHeader { get; set; }

        public EncryptedRecipientTransferInstructionSet TransferInstructionSet { get; set; }
        
        //Feed bolt-ons
        public EccEncryptedPayload EncryptedFeedPayload { get; set; }
        
        
        public PeerPayloadPackage PeerPayloadPackage { get; set; }
    }
}