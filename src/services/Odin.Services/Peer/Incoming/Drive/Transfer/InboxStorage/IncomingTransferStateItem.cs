using Odin.Core;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage
{
    public class IncomingTransferStateItem(TempFile tempFile, EncryptedRecipientTransferInstructionSet transferInstructionSet)
    {
        public EncryptedRecipientTransferInstructionSet TransferInstructionSet { get; init; } = transferInstructionSet;
        
        public TempFile TempFile { get; init; } = tempFile;
    }
}