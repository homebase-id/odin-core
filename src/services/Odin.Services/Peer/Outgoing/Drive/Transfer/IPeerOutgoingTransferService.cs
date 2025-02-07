using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage;
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
        Task<Dictionary<string, TransferStatus>> SendFile(InternalDriveFileId internalFile, TransitOptions options, TransferFileType transferFileType, FileSystemType fileSystemType, IOdinContext odinContext);

        Task<Dictionary<string, DeleteLinkedFileStatus>> SendDeleteFileRequest(InternalDriveFileId fileId, FileTransferOptions fileTransferOptions,
            IEnumerable<string> recipients, IOdinContext odinContext);

        Task<Dictionary<string, DeleteLinkedFileStatus>> SendDeleteFileRequest(GlobalTransitIdFileIdentifier remoteGlobalTransitIdentifier, FileTransferOptions fileTransferOptions,
            IEnumerable<string> recipients, IOdinContext odinContext);
        
        /// <summary>
        /// Sends a notification to the original sender indicating the file was read
        /// </summary>
        Task<SendReadReceiptResult> SendReadReceipt(List<InternalDriveFileId> files, IOdinContext odinContext,
            FileSystemType fileSystemType);
    }
}