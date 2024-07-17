using Odin.Core;
using Odin.Services.Drives;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage
{
    public class IncomingTransferStateItem(InternalDriveFileId tempFile, EncryptedRecipientTransferInstructionSet transferInstructionSet)
    {
        public EncryptedRecipientTransferInstructionSet TransferInstructionSet { get; init; } = transferInstructionSet;
        
        public InternalDriveFileId TempFile { get; set; } = tempFile;
    }
}