using System;
using System.Threading.Tasks;
using Youverse.Core.Identity;
using Youverse.Core.Services.Storage;
using Youverse.Core.Services.Transit.Quarantine;

namespace Youverse.Core.Services.Transit.Audit
{
    /// <summary>
    /// Write only auditor for tracking progress of an incoming transfer as it is processed (filtered, quarantined, stored, etc.)
    /// </summary>
    public interface ITransitAuditWriterService
    {
        /// <summary>
        /// Writes an event to the audit log.
        /// </summary>
        /// <param name="fileTrackerId"></param>
        /// <param name="auditEvent"></param>
        /// <param name="data"></param>
        /// <param name="message"></param>
        void WriteEvent(Guid fileTrackerId, TransitAuditEvent auditEvent);
        
        /// <summary>
        /// Writes an event to the audit log.
        /// </summary>
        /// <param name="fileTrackerId"></param>
        /// <param name="auditEvent"></param>
        /// <param name="filterId"></param>
        /// <param name="recommendation"></param>
        void WriteFilterEvent(Guid fileTrackerId, TransitAuditEvent auditEvent, Guid filterId, FilterAction recommendation);

        /// <summary>
        /// Creates an Id for tracking a series of events from the caller (i.e. the person sending a transfer)
        /// </summary>
        Task<Guid> CreateAuditTrackerId();
    }
}