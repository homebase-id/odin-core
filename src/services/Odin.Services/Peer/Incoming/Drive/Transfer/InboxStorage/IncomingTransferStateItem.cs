using Odin.Services.Drives;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage
{
    public class IncomingTransferStateItem(InternalDriveFileId file, bool isDirectWrite, EncryptedRecipientTransferInstructionSet transferInstructionSet)
    {
        public EncryptedRecipientTransferInstructionSet TransferInstructionSet { get; init; } = transferInstructionSet;

        public InternalDriveFileId File { get; init; } = file;

        /// <summary>
        /// True if the file was written to upload (temp) storage for direct write processing.
        /// False if the file was written to inbox storage.
        /// </summary>
        public bool IsDirectWrite { get; init; } = isDirectWrite;
    }
}
