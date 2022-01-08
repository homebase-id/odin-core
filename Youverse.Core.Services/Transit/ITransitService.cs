﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit.Outbox;
using Youverse.Core.Services.Transit.Upload;

namespace Youverse.Core.Services.Transit
{
    public interface ITransitService
    {

        /// <summary>
        /// Processes the instruction set on the specified packaged.  Used when all parts have been uploaded 
        /// </summary>
        /// <param name="package"></param>
        /// <returns></returns>
        Task<UploadResult> AcceptUpload(UploadPackage package);
        

        /// <summary>
        /// Accepts an incoming transfer as complete and valid.
        /// </summary>
        /// <param name="trackerId">The trackerId to be used during auditing</param>
        /// <param name="file">The file Id in storage</param>
        void AcceptTransfer(Guid trackerId, DriveFileId file);

        /// <summary>
        /// Sends a collection if <see cref="OutboxItem"/>s
        /// </summary>
        /// <param name="items"></param>
        /// <returns></returns>
        Task SendBatchNow(IEnumerable<OutboxItem> items);
    }
}