using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Youverse.Core.Services.Transit
{
    /// <summary>
    /// Holds the queue of transfer items that need to be encrypted when the app key is available
    /// </summary>
    public interface ITransferKeyEncryptionQueueService
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
        Task<IEnumerable<TransitKeyEncryptionQueueItem>> GetNext();
    }
}