using System;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Transit.Quarantine;

namespace Youverse.Core.Services.Transit.Audit
{
    public class LiteDbTransitAuditWriterService : ITransitAuditWriterService
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly ISystemStorage _systemStorage;


        public LiteDbTransitAuditWriterService(DotYouContextAccessor contextAccessor, ILogger<ITransitAuditWriterService> logger, ISystemStorage systemStorage)
        {
            _contextAccessor = contextAccessor;
            _systemStorage = systemStorage;
        }

        public Task<Guid> CreateAuditTrackerId()
        {
            //TODO: determine if i want to create a primary collection mapping a sender to their trackers or just rely on the long list written by WriteEvent
            var id = Guid.NewGuid();
            //var sender = this._context.GetCurrent().Caller.DotYouId;
            return Task.FromResult(id);
        }

        public void WriteEvent(Guid trackerId, TransitAuditEvent auditEvent)
        {
            var entry = new TransitAuditEntry()
            {
                Id = trackerId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Sender = this._contextAccessor.GetCurrent().Caller.DotYouId,
                EventId = (int) auditEvent,
            };

            StoreEntry(entry);
        }

        public void WriteFilterEvent(Guid trackerId, TransitAuditEvent auditEvent, Guid filterId, FilterAction recommendation)
        {
            Guard.Argument(filterId, nameof(filterId)).NotEqual(Guid.Empty);

            var entry = new TransitAuditEntry()
            {
                Id = trackerId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Sender = this._contextAccessor.GetCurrent().Caller.DotYouId,
                EventId = (int) auditEvent,
                FilterId = filterId,
                FilterRecommendation = recommendation,
            };

            StoreEntry(entry);
        }

        private void StoreEntry(TransitAuditEntry entry)
        {
            _systemStorage.WithTenantSystemStorage<TransitAuditEntry>(AuditConstants.AuditCollectionName, s => s.Save(entry));
        }
    }
}