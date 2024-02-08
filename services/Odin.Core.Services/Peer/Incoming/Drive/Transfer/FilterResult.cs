using System;

namespace Odin.Core.Services.Peer.Incoming.Drive.Transfer
{
    /// <summary>
    /// Resulting information from filtering a set of data
    /// </summary>
    public class FilterResult
    {
        /// <summary>
        /// The Id of the filter sending this response
        /// </summary>
        public Guid FilterId { get; set; }
        
        /// <summary>
        /// The action recommended by the filter
        /// </summary>
        public FilterAction Recommendation { get; set; }
        
        public string Message { get; set; }
    }
    
    
}