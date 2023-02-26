using System;
using System.Collections.Generic;
using System.IO;
using Youverse.Core.Identity;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Upload;

namespace Youverse.Core.Services.Transit.Outbox
{
    public class OutboxItem
    {
        public OutboxItem()
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

        public UInt64 AddedTimestamp { get; set; }

        public byte[] Marker { get; set; }

        /// <summary>
        /// Indicates the file should be read from the temp folder of the drive and deleted after it is sent to all recipients
        /// </summary>
        public bool IsTransientFile { get; set; }
        
        public RsaEncryptedRecipientTransferInstructionSet TransferInstructionSet { get; set; }
        
        /// <summary>
        /// TransitOptions provided when the file was sent by the client
        /// </summary>
        public TransitOptions OriginalTransitOptions { get; set; }

        /// <summary>
        /// Client Auth Token from the <see cref="IdentityConnectionRegistration"/> or Follower used to send the file to the recipient
        /// </summary>
        public byte[] EncryptedClientAuthToken { get; set; }
    }
}