namespace Odin.Services.Peer
{
    /// <summary>
    /// The response from sending a transfer or request to another DI host
    /// </summary>
    public class PeerTransferResponse
    {
        /// <summary>
        /// Specifies if the transfer was successfully received
        /// </summary>
        public PeerResponseCode Code { get; set; }
    }


    /// <summary>
    /// Various types of issues while calling to a remote identity over peer
    /// </summary>
    public enum PeerRequestIssueType
    {
        None = 0,
        DnsResolutionFailure = 10,
        ForbiddenWithInvalidRemoteIcr = 20,
        Forbidden = 30,
        ServiceUnavailable = 40,
        InternalServerError = 50,
        Unhandled = 3000,
    }
}