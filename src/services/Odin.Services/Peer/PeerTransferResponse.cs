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

        /// <summary>
        /// The version tag of the recipient's copy once a file sent to it is written at once
        /// (<see cref="PeerResponseCode.AcceptedDirectWrite"/>), so the sender can name it in its next change; null from
        /// the inbox, which writes later, and for an update, whose sender named the tag it gives the copy.
        /// </summary>
        public System.Guid? VersionTag { get; set; }
    }


    /// <summary>
    /// Various types of issues while calling to a remote identity over peer
    /// </summary>
    public enum PeerRequestIssueType
    {
        None = 0,

        /// <summary>
        /// Indicates something failed at the socket level (host not found, etc.)
        /// </summary>
        SocketError = 10,

        /// <summary>
        /// Remote server indicated the IdentityConnectionRegistration on their end was invalid or not found
        /// </summary>
        ForbiddenWithInvalidRemoteIcr = 20,

        /// <summary>
        /// Remove server returned 403, forbidden
        /// </summary>
        Forbidden = 30,

        ServiceUnavailable = 40,

        InternalServerError = 50,

        OperationCancelled = 60,

        HttpRequestFailed = 70,

        BadRequest = 90,
        
        Unhandled = 3000
    }
}