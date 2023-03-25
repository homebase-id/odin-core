using Youverse.Core.Services.Drives;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Transit.Encryption
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
    }
}