using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Transit.Audit;

namespace Youverse.Core.Services.Transit
{
    /// <summary>
    /// Base class for the transit subsystem providing various functions specific to Transit
    /// </summary>
    public abstract class TransitServiceBase : DotYouServiceBase
    {
        private readonly ITransitAuditWriterService _auditWriter;

        protected TransitServiceBase(DotYouContext context, ILogger logger, ITransitAuditWriterService auditWriter, IHubContext<NotificationHub, INotificationHub> notificationHub, DotYouHttpClientFactory fac) : base(context, logger, notificationHub, fac)
        {
            _auditWriter = auditWriter;
        }
        
        /// <summary>
        /// Access to the Audit writer
        /// </summary>
        protected ITransitAuditWriterService AuditWriter => _auditWriter;
    }
}