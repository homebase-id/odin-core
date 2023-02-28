using System;
using System.Threading.Tasks;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Incoming;

namespace Youverse.Core.Services.Transit
{
    /// <summary>
    /// Functions to manage incoming transfers from the transit system
    /// </summary>
    public interface ITransitReceiverService
    {
        /// <summary>
        /// Processes incoming transfers by converting their transfer keys and moving files to long term storage
        /// </summary>
        /// <param name="targetDrive"></param>
        /// <returns></returns>
        Task ProcessIncomingTransitInstructions(TargetDrive targetDrive);
        
        /// <summary>
        /// Gets a list of the items received by the transit which were quarantined.
        /// </summary>
        /// <param name="pageOptions"></param>
        /// <returns></returns>
        Task<PagedResult<TransferBoxItem>> GetQuarantinedItems(PageOptions pageOptions);
    }
}