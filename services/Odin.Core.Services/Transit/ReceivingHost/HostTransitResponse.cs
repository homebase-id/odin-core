namespace Odin.Core.Services.Transit.ReceivingHost
{
    /// <summary>
    /// The response from sending a transfer or request to another DI host
    /// </summary>
    public class HostTransitResponse
    {
        /// <summary>
        /// Specifies if the transfer was successfully received
        /// </summary>
        public TransitResponseCode Code { get; set; }
        
        public string Message { get; set; }
    }
}