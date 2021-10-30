namespace Youverse.Core.Services.Transit.Quarantine
{
    /// <summary>
    /// The final result after applying all filters to a all parts of a given data transfer
    /// </summary>
    public class CollectiveFilterResult
    {
        /// <summary>
        /// Specifies if the transfer was successfully received
        /// </summary>
        public FinalFilterAction Code { get; set; }
        
        public string Message { get; set; }
    }
}