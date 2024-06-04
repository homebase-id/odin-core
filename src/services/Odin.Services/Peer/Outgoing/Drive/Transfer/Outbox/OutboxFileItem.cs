using System;
using System.Collections.Generic;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Encryption;

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

        /// <summary>
        /// Indicates the file should be read from the temp folder of the drive and deleted after it is sent to all recipients
        /// </summary>
        public bool IsTransientFile { get; set; }
        
        public EncryptedRecipientTransferInstructionSet TransferInstructionSet { get; set; }
        
        /// <summary>
        /// TransitOptions provided when the file was sent by the client
        /// </summary>
        public TransitOptions OriginalTransitOptions { get; set; }

        /// <summary>
        /// Client Auth Token from the <see cref="IdentityConnectionRegistration"/> or Follower used to send the file to the recipient
        /// </summary>
        public byte[] EncryptedClientAuthToken { get; set; }

        public OutboxItemType Type { get; set; }
        public int AttemptCount { get; set; }
        public byte[] RawValue { get; set; }
        public Guid? DependencyFileId { get; set; }
    }
}