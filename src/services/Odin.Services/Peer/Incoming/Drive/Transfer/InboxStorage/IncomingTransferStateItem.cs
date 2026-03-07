using System;
using Odin.Core;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage
{
    public class IncomingTransferStateItem(UploadFile uploadFile, InboxFile inboxFile, EncryptedRecipientTransferInstructionSet transferInstructionSet)
    {
        public EncryptedRecipientTransferInstructionSet TransferInstructionSet { get; init; } = transferInstructionSet;
        
        public UploadFile UploadFile { get; init; } = uploadFile;
        public InboxFile InboxFile { get; init; } = inboxFile;
        
        public InternalDriveFileId? FileId => UploadFile?.FileId ?? InboxFile?.FileId;
        public Guid DriveId => FileId?.DriveId ?? Guid.Empty;
    }
}
