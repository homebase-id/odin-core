﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Identity;
using Youverse.Core.Services.Drives;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Transit.SendingHost
{
    public interface ITransitService
    {
        /// <summary>
        /// Sends the specified file
        /// </summary>
        /// <returns></returns>
        Task<Dictionary<string, TransferStatus>> SendFile(InternalDriveFileId internalFile, TransitOptions options, TransferFileType transferFileType, FileSystemType fileSystemType,
            ClientAccessTokenSource tokenSource = ClientAccessTokenSource.Circle);

        /// <summary>
        /// Processes and sends any files in the outbox across all drives
        /// </summary>
        Task ProcessOutbox();

        /// <summary>
        /// Notifies the recipients the file with the <param name="globalTransitId"/> must be deleted
        /// </summary>
        Task<Dictionary<string, TransitResponseCode>> SendDeleteLinkedFileRequest(GlobalTransitIdFileIdentifier remoteGlobalTransitIdentifier, SendFileOptions sendFileOptions,
            IEnumerable<string> recipients);
    }
}