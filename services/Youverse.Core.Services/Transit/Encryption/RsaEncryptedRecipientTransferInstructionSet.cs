using System;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.FileSystem;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Transit.Encryption
{
    /// <summary>
    /// The encrypted version of the KeyHeader for a given recipient
    /// which as been encrypted using the RecipientTransitPublicKey
    /// </summary>
    public class RsaEncryptedRecipientTransferInstructionSet
    {
        public UInt32 PublicKeyCrc { get; set; }

        public byte[] EncryptedAesKeyHeader { get; set; }

        public TargetDrive TargetDrive { get; set; }

        public SendContents OriginalSendContents { get; set; }

        public TransferFileType TransferFileType { get; set; }
        
        public FileSystemType FileSystemType { get; set; }
    }
}