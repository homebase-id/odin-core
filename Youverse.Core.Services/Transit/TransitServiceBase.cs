using Youverse.Core.Services.Transit.Audit;

namespace Youverse.Core.Services.Transit
{
    /// <summary>
    /// Base class for the transit subsystem providing various functions specific to Transit
    /// </summary>
    public abstract class TransitServiceBase<T> 
    {
        private readonly ITransitAuditWriterService _auditWriter;

        protected TransitServiceBase(ITransitAuditWriterService auditWriter)
        {
            _auditWriter = auditWriter;
        }
        
        /// <summary>
        /// Access to the Audit writer
        /// </summary>
        protected ITransitAuditWriterService AuditWriter => _auditWriter;
    }
}