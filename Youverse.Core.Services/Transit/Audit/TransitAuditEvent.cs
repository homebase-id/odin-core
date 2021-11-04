namespace Youverse.Core.Services.Transit.Audit
{
    public enum TransitAuditEvent
    {
        /// <summary>
        /// Indicates a filter was applied to the data.
        /// </summary>
        FilterApplied = 10,

        /// <summary>
        /// Indicates when all filters were applied
        /// </summary>
        AllFiltersApplied = 20,

        /// <summary>
        /// Indicates a transfer was accepted
        /// </summary>
        Accepted = 30,

        /// <summary>
        /// Indicates a transfer was quarantined.
        /// </summary>
        Quarantined = 50,

        /// <summary>
        /// Indicates a transfer was rejected
        /// </summary>
        Rejected = 80
    }
}