using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Services.Drives;
using Odin.Core.Storage;

namespace Odin.Core.Services.Peer.Outgoing
{
    public interface ITransitService
    {
        /// <summary>
        /// Sends the specified file
        /// </summary>
        /// <returns></returns>
        Task<Dictionary<string, TransferStatus>> SendFile(InternalDriveFileId internalFile, TransitOptions options, TransferFileType transferFileType, FileSystemType fileSystemType);

        /// <summary>
        /// Processes and sends any files in the outbox across all drives
        /// </summary>
        Task ProcessOutbox();

        /// <summary>
        /// Notifies the recipients the file with the <param name="remoteGlobalTransitIdentifier"/> must be deleted
        /// </summary>
        Task<Dictionary<string, PeerResponseCode>> SendDeleteFileRequest(GlobalTransitIdFileIdentifier remoteGlobalTransitIdentifier, SendFileOptions sendFileOptions,
            IEnumerable<string> recipients);
    }
}