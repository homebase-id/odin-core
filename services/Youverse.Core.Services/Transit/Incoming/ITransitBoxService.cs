using System;
using System.Collections.Generic;
using System.Threading.Tasks;


namespace Youverse.Core.Services.Transit.Incoming
{
    /// <summary>
    /// Items in the Inbox for a given tenant
    /// </summary>
    public interface ITransitBoxService
    {
        /// <summary>
        /// Adds an item to be encrypted and moved to the Inbox
        /// </summary>
        /// <param name="item"></param>
        Task Add(TransferBoxItem item);

        /// <summary>
        /// Gets a list of all items
        /// </summary>
        /// <returns></returns>
        Task<List<TransferBoxItem>> GetPendingItems(Guid driveId);

        /// <summary>
        /// Indicates the item represented by the marker has been transfered
        /// </summary>
        Task MarkComplete(Guid driveId, byte[] marker);
        
        /// <summary>
        /// Removes an item from the Inbox.
        /// </summary>
        Task MarkFailure(Guid driveId, byte[] marker);
    }
}