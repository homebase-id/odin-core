using System;
using Youverse.Core.Identity;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Transit.Incoming
{
    public class TransferBoxItem
    {
        public TransferBoxItem()
        {
            this.Id = Guid.NewGuid();
        }

        public Guid Id { get; set; }

        public UInt64 AddedTimestamp { get; set; }
        
        /// <summary>
        /// The application that created this outbox item
        /// </summary>
        public Guid AppId { get; set; }

        public DotYouIdentity Sender { get; set; }

        public DriveFileId TempFile { get; set; }

        /// <summary>
        /// The Id used to track the reception of this inbox item
        /// </summary>
        public Guid TrackerId { get; set; }

        /// <summary>
        /// Specifies how this item should be prioritized by the Outbox Sending
        /// Process.  The lower the number, the higher the priority
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// The CRC of the <see cref="TransitPublicKey"/> used by the sender
        /// </summary>
        public uint PublicKeyCrc { get; set; }
    }
}