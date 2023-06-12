﻿using System;
using Odin.Core.Identity;
using Odin.Core.Services.EncryptionKeyService;
using Odin.Core.Services.Transit.SendingHost;
using Odin.Core.Storage;
using Odin.Core.Time;

namespace Odin.Core.Services.Transit.ReceivingHost.Incoming
{
    public class TransferInboxItem
    {
        public TransferInboxItem()
        {
            this.Id = Guid.NewGuid();
        }

        public Guid Id { get; set; }

        /// <summary>
        /// The CRC of the <see cref="TransitPublicKey"/> used by the sender
        /// </summary>
        // public uint PublicKeyCrc { get; set; }

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
        
        // public byte[] RsaEncryptedKeyHeader { get; set; }
        public RsaEncryptedPayload RsaEncryptedKeyHeaderPayload { get; set; }
    }
}