using System;
using System.Threading.Tasks;
using Youverse.Core.Identity;
using Youverse.Core.Services.Transit.Quarantine;

namespace Youverse.Core.Services.Transit.Audit
{
    /// <summary>
    /// Write events to the transit audit log
    /// </summary>
    public interface ITransitAuditWriterService
    {
        /// <summary>
        /// Creates an Id for tracking a series of events from the caller (i.e. the person sending a transfer)
        /// </summary>
        Task<Guid> CreateAuditTrackerId();
        
        /// <summary>
        /// Writes an event to the audit log.
        /// </summary>
        /// <param name="trackerId"></param>
        /// <param name="auditEvent"></param>
        void WriteEvent(Guid trackerId, TransitAuditEvent auditEvent);
        
        /// <summary>
        /// Writes an event to the audit log.
        /// </summary>
        /// <param name="trackerId"></param>
        /// <param name="auditEvent"></param>
        /// <param name="filterId"></param>
        /// <param name="recommendation"></param>
        void WriteFilterEvent(Guid trackerId, TransitAuditEvent auditEvent, Guid filterId, FilterAction recommendation);
    }
}