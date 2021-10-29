using System;

namespace Youverse.Core.Services.Transit.Quarantine
{
    /// <summary>
    /// Resulting information from filtering a set of data
    /// </summary>
    public class FilterResponse
    {
        /// <summary>
        /// The Id of the filter sending this response
        /// </summary>
        public Guid FilterId { get; set; }
        
        /// <summary>
        /// The action recommended by the filter
        /// </summary>
        public SuggestedAction SuggestedAction { get; set; }
        
        public string Message { get; set; }
    }

    public enum SuggestedAction
    {
        None = 0,
        Quarantine = 1,
        Reject = 2,
    }
}