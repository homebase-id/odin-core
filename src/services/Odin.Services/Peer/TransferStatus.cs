
namespace Odin.Services.Peer
{
    public enum TransferStatus
    {
        /// <summary>
        /// Indicates creating the outbox item failed.  The client should retry.
        /// </summary>
        EnqueuedFailed = 1,
        
        /// <summary>
        /// Item is enqueued in the outbox and will be sent shortly
        /// </summary>
        Enqueued = 3,

        /// <summary>
        /// Indicates the transfer was successfully delivered to the inbox.
        /// </summary>
        DeliveredToInbox = 5,
        
        /// <summary>
        /// Recipient server rejected the transfer, client should retry 
        /// </summary>
        TotalRejectionClientShouldRetry = 9,
        
        /// <summary>
        /// Indicates the recipient server returned a security error
        /// </summary>
        RecipientReturnedAccessDenied = 13,
        
    }
}