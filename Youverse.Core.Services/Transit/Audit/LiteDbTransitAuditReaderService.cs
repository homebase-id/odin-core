using System;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Transit.Audit
{
    public class LiteDbTransitAuditReaderService : DotYouServiceBase, ITransitAuditReaderService
    {
        public LiteDbTransitAuditReaderService(DotYouContext context, ILogger logger) : base(context, logger, null, null)
        {
        }

        public async Task<PagedResult<TransitAuditEntry>> GetList(DateRangeOffset range, PageOptions pageOptions)
        {
            Expression<Func<TransitAuditEntry, bool>> predicate = (entry => range.IsBetween(entry.Timestamp, true));
            Expression<Func<TransitAuditEntry, Int64>> sortKey = (entry => entry.Timestamp);

            var list = await WithTenantSystemStorageReturnList<TransitAuditEntry>(
                AuditConstants.AuditCollectionName,
                s => s.Find(predicate, ListSortDirection.Descending, sortKey, pageOptions));
            
            return list;
        }

        public async Task<PagedResult<TransitAuditEntry>> GetList(TimeSpan withInTimespan, PageOptions pageOptions)
        {
            var start = DateTimeOffset.UtcNow.Subtract(withInTimespan);
            var end = DateTimeOffset.UtcNow;
            var range = new DateRangeOffset(start, end);
            return await this.GetList(range, pageOptions);
        }
    }
}