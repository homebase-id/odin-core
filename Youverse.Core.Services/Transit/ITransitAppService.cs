using System;
using System.Threading.Tasks;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Incoming;

namespace Youverse.Core.Services.Transit
{
    /// <summary>
    /// Functions to manage incoming transfers from the transit system
    /// </summary>
    public interface ITransitAppService
    {
        /// <summary>
        /// Converts a transfer key header to a long term key header and stores it for the specified file.
        /// </summary>
        Task StoreLongTerm(InternalDriveFileId file);

        /// <summary>
        /// Processes incoming transfers by converting their transfer keys and moving files to long term storage
        /// </summary>
        /// <returns></returns>
        [Obsolete("TODO: replace with new outbox process")]
        Task ProcessTransfers();

        /// <summary>
        /// Gets a list of the items received by the transit system having passed all filters
        /// </summary>
        /// <param name="pageOptions"></param>
        /// <returns></returns>
        Task<PagedResult<TransferBoxItem>> GetAcceptedItems(PageOptions pageOptions);
        
        /// <summary>
        /// Gets a list of the items received by the transit which were quarantined.
        /// </summary>
        /// <param name="pageOptions"></param>
        /// <returns></returns>
        Task<PagedResult<TransferBoxItem>> GetQuarantinedItems(PageOptions pageOptions);
    }
}