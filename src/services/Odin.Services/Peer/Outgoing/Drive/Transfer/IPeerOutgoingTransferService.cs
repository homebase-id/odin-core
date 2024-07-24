using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.FileSystem.Base.Upload.Attachments;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer
{
    public interface IPeerOutgoingTransferService
    {
        /// <summary>
        /// Sends the specified file
        /// </summary>
        /// <returns></returns>
        Task<Dictionary<string, TransferStatus>> SendFile(InternalDriveFileId internalFile, TransitOptions options, TransferFileType transferFileType,
            StorageIntent storageIntent, FileSystemType fileSystemType, IOdinContext odinContext, DatabaseConnection cn);

        Task<Dictionary<string, DeleteLinkedFileStatus>> SendDeleteFileRequest(InternalDriveFileId fileId, FileTransferOptions fileTransferOptions,
            IEnumerable<string> recipients, IOdinContext odinContext, DatabaseConnection cn);

        Task<Dictionary<string, DeleteLinkedFileStatus>> SendDeleteFileRequest(GlobalTransitIdFileIdentifier remoteGlobalTransitIdentifier, FileTransferOptions fileTransferOptions,
            IEnumerable<string> recipients, IOdinContext odinContext, DatabaseConnection cn);
        
        /// <summary>
        /// Sends a notification to the original sender indicating the file was read
        /// </summary>
        Task<SendReadReceiptResult> SendReadReceipt(List<InternalDriveFileId> files, IOdinContext odinContext, DatabaseConnection cn,
            FileSystemType fileSystemType);

        Task<Dictionary<string, TransferStatus>> SendPayload(InternalDriveFileId file, PeerDirectUploadPayloadInstructionSet uploadInstructionSet,
            FileSystemType fileSystemType,
            IOdinContext odinContext, DatabaseConnection cn);
    }
}