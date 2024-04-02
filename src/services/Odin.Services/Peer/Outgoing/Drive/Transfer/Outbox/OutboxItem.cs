using System;
using System.Collections.Generic;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox
{
    public class OutboxItem
    {
        public OutboxItem()
        {
            this.AddedTimestamp = UnixTimeUtc.Now().seconds;
            this.File = new InternalDriveFileId();
        }

        public OdinId Recipient { get; set; }

        public InternalDriveFileId File { get; set; }

        /// <summary>
        /// Specifies how this item should be prioritized by the Outbox Sending
        /// Process.  The lower the number, the higher the priority
        /// </summary>
        public int Priority { get; set; }

        public OutboxItemType Type { get; set; } = OutboxItemType.File;
        
        public Int64 AddedTimestamp { get; set; }

        public Guid Marker { get; set; }
        
        public EncryptedRecipientTransferInstructionSet TransferInstructionSet { get; set; }

        /// <summary>
        /// TransitOptions provided when the file was sent by the client
        /// </summary>
        public TransitOptions OriginalTransitOptions { get; set; }

        /// <summary>
        /// Client Auth Token from the <see cref="IdentityConnectionRegistration"/> or Follower used to send the file to the recipient
        /// </summary>
        public byte[] EncryptedClientAuthToken { get; set; }

        public int AttemptCount { get; set; }

        public byte[] RawValue { get; set; }
    }
}