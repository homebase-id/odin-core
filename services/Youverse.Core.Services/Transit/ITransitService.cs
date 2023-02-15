using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drives.FileSystem;
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
        Task<Dictionary<string, TransferStatus>> SendFile(InternalDriveFileId internalFile, TransitOptions options, TransferFileType transferFileType, FileSystemType fileSystemType, ClientAccessTokenSource tokenSource = ClientAccessTokenSource.Circle);

        /// <summary>
        /// Processes and sends any files in the outbox across all drives
        /// </summary>
        /// <param name="batchSize"></param>
        Task ProcessOutbox(int batchSize);

        /// <summary>
        /// Notifies the recipients the file with the <param name="globalTransitId"/> must be deleted
        /// </summary>
        Task<Dictionary<string, TransitResponseCode>> SendDeleteLinkedFileRequest(Guid driveId, Guid globalTransitId, IEnumerable<string> recipients);

    }
}