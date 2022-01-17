using Youverse.Core.Services.Transit.Quarantine;

namespace Youverse.Core.Services.Transit
{
    /// <summary>
    /// The response from sending a transfer to another DI host
    /// </summary>
    public class HostTransferResponse
    {
        /// <summary>
        /// Specifies if the transfer was successfully received
        /// </summary>
        public TransitResponseCode Code { get; set; }
        
        public string Message { get; set; }
    }
}