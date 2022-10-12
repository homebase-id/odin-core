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

        /// <summary>
        /// The CRC of the <see cref="TransitPublicKey"/> used by the sender
        /// </summary>
        public uint PublicKeyCrc { get; set; }

        public InternalDriveFileId TempFile { get; set; }

        public UnixTimeUtcSeconds AddedTimestamp { get; set; }

        public DotYouIdentity Sender { get; set; }

        /// <summary>
        /// Specifies how this item should be prioritized by the Outbox Sending
        /// Process.  The lower the number, the higher the priority
        /// </summary>
        public int Priority { get; set; }

        public byte[] Marker { get; set; }
    }
}