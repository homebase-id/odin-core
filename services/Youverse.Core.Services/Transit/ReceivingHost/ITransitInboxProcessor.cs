using System.Threading.Tasks;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Transit.ReceivingHost.Incoming;

namespace Youverse.Core.Services.Transit.ReceivingHost
{
    /// <summary>
    /// Processes files received over transit that are stored in the inbox
    /// </summary>
    public interface ITransitInboxProcessor
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
        Task<PagedResult<TransferInboxItem>> GetQuarantinedItems(PageOptions pageOptions);
    }
}