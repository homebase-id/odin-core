
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
    }
}