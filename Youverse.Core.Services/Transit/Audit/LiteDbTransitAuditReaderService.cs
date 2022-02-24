using System;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Transit.Audit
{
    public class LiteDbTransitAuditReaderService : ITransitAuditReaderService
    {
        private readonly ISystemStorage _systemStorage;

        public LiteDbTransitAuditReaderService(DotYouContextAccessor contextAccessor, ILogger<ITransitAuditReaderService> logger, ISystemStorage systemStorage)
        {
            _systemStorage = systemStorage;
        }

        public async Task<PagedResult<TransitAuditEntry>> GetList(DateRangeOffset range, PageOptions pageOptions)
        {
            Expression<Func<TransitAuditEntry, bool>> predicate = (entry => range.StartDateTimeOffsetMilliseconds <= entry.Timestamp && range.EndDateTimeOffsetMilliseconds >= entry.Timestamp);
            Expression<Func<TransitAuditEntry, Int64>> sortKey = (entry => entry.Timestamp);

            var list = await _systemStorage.WithTenantSystemStorageReturnList<TransitAuditEntry>(
                AuditConstants.AuditCollectionName,
                s => s.Find(predicate, ListSortDirection.Descending, sortKey, pageOptions));

            var all = await _systemStorage.WithTenantSystemStorageReturnList<TransitAuditEntry>(
                AuditConstants.AuditCollectionName,
                s => s.Find(p => true, ListSortDirection.Descending, sortKey, pageOptions));


            return list;
        }

        public async Task<PagedResult<TransitAuditEntry>> GetList(TimeSpan withInTimespan, PageOptions pageOptions)
        {
            var now = DateTimeOffset.UtcNow;
            var start = now.Subtract(withInTimespan);
            var range = new DateRangeOffset(start, now);
            return await this.GetList(range, pageOptions);
        }
    }
}