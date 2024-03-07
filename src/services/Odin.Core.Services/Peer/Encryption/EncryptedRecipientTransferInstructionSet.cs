using Odin.Core.Services.Drives;
using Odin.Core.Services.Peer.Outgoing;
using Odin.Core.Services.Peer.Outgoing.Drive;
using Odin.Core.Storage;

namespace Odin.Core.Services.Peer.Encryption
{
    /// <summary>
    /// The encrypted version of the KeyHeader for a given recipient
    /// which as been encrypted using the RecipientTransitPublicKey
    /// </summary>
    public class EncryptedRecipientTransferInstructionSet
    {
        public TargetDrive TargetDrive { get; set; }

        public TransferFileType TransferFileType { get; set; }

        public FileSystemType FileSystemType { get; set; }

        /// <summary>
        /// The file's KeyHeader encrypt4ed with the shared secret indicated by the recipient
        /// </summary>
        public EncryptedKeyHeader SharedSecretEncryptedKeyHeader { get; set; }

        /// <summary>
        /// The file parts provided by the sender
        /// </summary>
        public SendContents ContentsProvided { get; set; }

        public AppNotificationOptions AppNotificationOptions { get; set; }

        public bool IsValid()
        {
            if (null == this.SharedSecretEncryptedKeyHeader)
            {
                return false;
            }

            var isValid = this.TargetDrive.IsValid() &&
                          this.SharedSecretEncryptedKeyHeader.Iv?.Length > 0 &&
                          this.SharedSecretEncryptedKeyHeader.EncryptedAesKey?.Length > 0;

            return isValid;
        }
    }
}