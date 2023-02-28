using System;
using Youverse.Core.Identity;
using Youverse.Core.Services.Drives.FileSystem;
using Youverse.Core.Storage;

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

        public byte[] Marker { get; set; }

        /// <summary>
        /// The FileSystemType of the incoming file
        /// </summary>
        public FileSystemType FileSystemType { get; set; }
    }
}