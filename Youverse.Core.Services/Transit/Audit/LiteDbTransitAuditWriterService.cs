using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Transit.Quarantine;

namespace Youverse.Core.Services.Transit.Audit
{
    public class LiteDbTransitAuditWriterService : DotYouServiceBase, ITransitAuditWriterService
    {
        private const string AuditCollectionName = "TransitAudit";
        public LiteDbTransitAuditWriterService(DotYouContext context, ILogger logger) : base(context, logger, null, null)
        {
        }
        
        public void WriteEvent(Guid fileTrackerId, TransitAuditEvent auditEvent)
        {
            //var sender = this.Context.Caller.DotYouId;

            throw new NotImplementedException();
        }

        public void WriteFilterEvent(Guid fileTrackerId, TransitAuditEvent auditEvent, Guid filterId, FilterAction recommendation)
        {
            //var sender = this.Context.Caller.DotYouId;

            throw new NotImplementedException();
        }

        public async Task<Guid> CreateAuditTrackerId()
        {
            //var sender = this.Context.Caller.DotYouId;
            throw new NotImplementedException();
        }
    }
}