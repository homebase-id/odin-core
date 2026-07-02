using System;

namespace Odin.Services.Membership.Connections.Requests
{
    /// <summary>
    /// Sent to a recipient's perimeter when the caller cancels a connection request they previously sent, so the
    /// recipient can remove the matching pending request instead of leaving it stranded.
    /// </summary>
    public class ConnectionRequestWithdrawal
    {
        /// <summary>
        /// Identifies the specific request instance being withdrawn (the outgoing EccEncryptedPayload.TimestampId).
        /// The recipient only removes its pending request when this matches the one it currently holds, so a stale
        /// withdrawal cannot delete a newer re-sent request.
        /// </summary>
        public Guid TimestampId { get; set; }
    }
}
