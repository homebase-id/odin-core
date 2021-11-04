namespace Youverse.Core.Services.Transit.Audit
{
    public enum TransitAuditEvent
    {
        /// <summary>
        /// Indicates a filter was applied to the data.
        /// </summary>
        FilterApplied,
        
        /// <summary>
        /// Indicates when all filters were applied
        /// </summary>
        AllFiltersApplied,
        
        /// <summary>
        /// Indicates a transfer was accepted
        /// </summary>
        Accepted,
        
        /// <summary>
        /// Indicates a transfer was quarantined.
        /// </summary>
        Quarantined,
        
        /// <summary>
        /// Indicates a transfer was rejected
        /// </summary>
        Rejected
    }
}