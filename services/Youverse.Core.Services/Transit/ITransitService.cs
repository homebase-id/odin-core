using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Identity;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit.Outbox;
using Youverse.Core.Services.Transit.Upload;

namespace Youverse.Core.Services.Transit
{
    public interface ITransitService
    {
        /// <summary>
        /// Sends the specified file
        /// </summary>
        /// <returns></returns>
        Task<Dictionary<string, TransferStatus>> SendFile(InternalDriveFileId internalFile, TransitOptions options, TransferFileType transferFileType);

        /// <summary>
        /// Accepts an incoming file as complete and valid.
        /// </summary>
        Task AcceptTransfer(InternalDriveFileId file, uint publicKeyCrc);

        /// <summary>
        /// Sends a collection if <see cref="OutboxItem"/>s.  Returns the results of each recipient
        /// </summary>
        /// <param name="items"></param>
        /// <returns></returns>
        Task<List<SendResult>> SendBatchNow(IEnumerable<OutboxItem> items);

        /// <summary>
        /// Processes and sends any files in the outbox across all drives
        /// </summary>
        /// <param name="batchSize"></param>
        Task ProcessOutbox(int batchSize);

        /// <summary>
        /// Notifies the recipients the file with the <param name="globalTransitId"/> must be deleted
        /// </summary>
        Task<Dictionary<string, TransitResponseCode>> SendDeleteLinkedFileRequest(Guid driveId, Guid globalTransitId, IEnumerable<string> recipients);

        /// <summary>
        /// Deletes the requested file.
        /// </summary>
        Task AcceptDeleteLinkedFileRequest(Guid driveId, Guid globalTransitId);
    }
}