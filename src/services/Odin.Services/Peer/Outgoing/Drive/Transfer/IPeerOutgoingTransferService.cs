using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer
{
    public interface IPeerOutgoingTransferService
    {
        /// <summary>
        /// Sends the specified file
        /// </summary>
        /// <returns></returns>
        Task<Dictionary<string, TransferStatus>> SendFile(InternalDriveFileId internalFile, TransitOptions options, TransferFileType transferFileType, FileSystemType fileSystemType, IOdinContext odinContext, IdentityDatabase db);

        Task<Dictionary<string, DeleteLinkedFileStatus>> SendDeleteFileRequest(InternalDriveFileId fileId, FileTransferOptions fileTransferOptions,
            IEnumerable<string> recipients, IOdinContext odinContext, IdentityDatabase db);

        Task<Dictionary<string, DeleteLinkedFileStatus>> SendDeleteFileRequest(GlobalTransitIdFileIdentifier remoteGlobalTransitIdentifier, FileTransferOptions fileTransferOptions,
            IEnumerable<string> recipients, IOdinContext odinContext, IdentityDatabase db);
        
        /// <summary>
        /// Sends a notification to the original sender indicating the file was read
        /// </summary>
        Task<SendReadReceiptResult> SendReadReceipt(List<InternalDriveFileId> files, IOdinContext odinContext, IdentityDatabase db,
            FileSystemType fileSystemType);
    }
}