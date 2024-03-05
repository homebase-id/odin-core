using Odin.Core.Services.Drives;
using Odin.Core.Services.Peer.Encryption;

namespace Odin.Core.Services.Peer.Incoming.Drive.Transfer.InboxStorage
{
    public class IncomingTransferStateItem(GuidId id, InternalDriveFileId tempFile, EncryptedRecipientTransferInstructionSet transferInstructionSet)
    {
        public EncryptedRecipientTransferInstructionSet TransferInstructionSet { get; init; } = transferInstructionSet;

        public GuidId Id { get; init; } = id;

        public InternalDriveFileId TempFile { get; set; } = tempFile;
    }
}