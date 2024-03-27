using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage;
using Odin.Services.Drives;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer
{
    public interface IPeerOutgoingTransferService
    {
        /// <summary>
        /// Sends the specified file
        /// </summary>
        /// <returns></returns>
        Task<Dictionary<string, TransferStatus>> SendFile(InternalDriveFileId internalFile, TransitOptions options, TransferFileType transferFileType, FileSystemType fileSystemType);
        
        /// <summary>
        /// Notifies the recipients the file with the <param name="remoteGlobalTransitIdentifier"/> must be deleted
        /// </summary>
        Task<Dictionary<string, DeleteLinkedFileStatus>> SendDeleteFileRequest(GlobalTransitIdFileIdentifier remoteGlobalTransitIdentifier, FileTransferOptions fileTransferOptions,
            IEnumerable<string> recipients);
    }
}