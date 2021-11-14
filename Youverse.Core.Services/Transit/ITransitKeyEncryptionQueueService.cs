using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Youverse.Core.Services.Storage;

namespace Youverse.Core.Services.Transit
{
    public interface ITransitKeyEncryptionQueueService
    {
        /// <summary>
        /// Adds an <see cref="TransitKeyEncryptionQueueItem"/> to be processed.
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        Task Enqueue(TransitKeyEncryptionQueueItem item);
        
        /// <summary>
        /// Gets the next <param name="count">number of </param>items to be processed
        /// </summary>
        /// <returns></returns>
        Task<IEnumerable<TransitKeyEncryptionQueueItem>> GetNext(PageOptions pageOptions);
    }
}