﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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
        /// <param name="fileId">The file Id in storage</param>
        void Accept(Guid trackerId, Guid fileId);

        /// <summary>
        /// Sends a collection if <see cref="OutboxItem"/>s
        /// </summary>
        /// <param name="items"></param>
        /// <returns></returns>
        Task<TransferResult> SendBatchNow(IEnumerable<OutboxItem> items);
    }
}