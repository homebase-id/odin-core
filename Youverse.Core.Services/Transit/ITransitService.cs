using System;
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
        /// Prepares to transfer an <see cref="UploadPackage"/> by generating Recipient Transfer Keys and
        /// placing the transfer <see cref="IOutboxService"/>.
        /// </summary>
        /// <returns></returns>
        Task<TransferResult> PrepareTransfer(UploadPackage package);

        /// <summary>
        /// Accepts an incoming transfer as complete and valid.
        /// </summary>
        /// <param name="trackerId">The trackerId to be used during auditing</param>
        /// <param name="file">The file Id in storage</param>
        void Accept(Guid trackerId, DriveFileId file);

        /// <summary>
        /// Sends a collection if <see cref="OutboxItem"/>s
        /// </summary>
        /// <param name="items"></param>
        /// <returns></returns>
        Task SendBatchNow(IEnumerable<OutboxItem> items);
    }
}