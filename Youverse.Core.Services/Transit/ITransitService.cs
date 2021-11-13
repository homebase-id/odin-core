using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Youverse.Core.Services.Transit
{
    public interface ITransitService
    {
        Task<TransferResult> SendBatchNow(IEnumerable<TransferQueueItem> queuedItems);
        
        Task<TransferResult> SendBatchNow(RecipientList recipients, Guid fileId);

        /// <summary>
        /// Sends an envelope to a list of recipients.  TODO: document the default behavior for how this decides send priority
        /// </summary>
        /// <returns></returns>
        Task<TransferResult> Send(UploadPackage uploadPackage);

        /// <summary>
        /// Accepts a transfer as complete and valid.
        /// </summary>
        /// <param name="trackerId">The trackerId to be used during auditing</param>
        /// <param name="fileId">The file Id in storage</param>
        void Accept(Guid trackerId, Guid fileId);
    }
}