using System;
using System.Threading.Tasks;

namespace Youverse.Core.Services.Transit.Audit
{
    /// <summary>
    /// Read Transit events from the transit audit log
    /// </summary>
    public interface ITransitAuditReaderService
    {
        /// <summary>
        /// Gets a set of events
        /// </summary>
        /// <param name="range">Range indicating the start and end dates</param>
        /// <param name="pageOptions"></param>
        /// <returns></returns>
        Task<PagedResult<TransitAuditEntry>> GetList(DateRangeOffset range, PageOptions pageOptions);

        /// <summary>
        /// Gets a set of events
        /// </summary>
        /// <param name="withInTimespan">Timespan indicating how far to go back in time</param>
        /// <param name="pageOptions"></param>
        /// <returns></returns>
        Task<PagedResult<TransitAuditEntry>> GetList(TimeSpan withInTimespan, PageOptions pageOptions);
    }
}