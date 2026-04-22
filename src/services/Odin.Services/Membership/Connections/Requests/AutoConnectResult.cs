namespace Odin.Services.Membership.Connections.Requests
{
    public enum AutoConnectOutcome
    {
        /// <summary>
        /// The connection request was sent, auto-accepted by the recipient, and the ICR was
        /// fully established on both sides within this call.
        /// </summary>
        Connected = 1,

        /// <summary>
        /// A pending incoming request from the recipient already existed; it was accepted
        /// locally without making a peer call.
        /// </summary>
        AcceptedFromExistingIncoming = 2,

        /// <summary>
        /// The request was sent and stored as pending on the recipient, but the recipient
        /// did not auto-accept (e.g., auto-accept is disabled or eligibility was not met).
        /// </summary>
        PendingManualApproval = 3,

        /// <summary>
        /// An ICR with the recipient already exists in a connected state.
        /// </summary>
        AlreadyConnected = 4,

        /// <summary>
        /// The recipient is blocked, or we are blocked by the recipient.
        /// </summary>
        Blocked = 5,

        /// <summary>
        /// An outgoing connection request to the recipient already exists.
        /// </summary>
        OutgoingRequestAlreadyExists = 6,

        /// <summary>
        /// The recipient reports an introductory request from us was already received.
        /// </summary>
        DuplicateIntroductoryRequest = 7,

        /// <summary>
        /// Could not reach the recipient (transport / encryption failure).
        /// </summary>
        RecipientUnreachable = 8,

        /// <summary>
        /// The recipient explicitly denied the request (Forbidden or equivalent).
        /// </summary>
        RecipientRejected = 9,

        /// <summary>
        /// The request header failed validation (self-recipient, etc.).
        /// </summary>
        InvalidRequest = 10,

        /// <summary>
        /// An unexpected error occurred. See <see cref="AutoConnectResult.Detail"/>.
        /// </summary>
        Failed = 99,
    }

    public class AutoConnectResult
    {
        public AutoConnectOutcome Outcome { get; set; }

        /// <summary>
        /// Optional diagnostic string; populated for <see cref="AutoConnectOutcome.Failed"/>
        /// or <see cref="AutoConnectOutcome.RecipientUnreachable"/>.
        /// </summary>
        public string Detail { get; set; }
    }
}
