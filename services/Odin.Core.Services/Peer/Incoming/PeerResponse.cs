namespace Odin.Core.Services.Peer.Incoming
{
    /// <summary>
    /// The response from sending a transfer or request to another DI host
    /// </summary>
    public class PeerResponse
    {
        /// <summary>
        /// Specifies if the transfer was successfully received
        /// </summary>
        public PeerResponseCode Code { get; set; }
        
        public string Message { get; set; }
    }
}