using System;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.Peer
{
    public enum TransferStatus
    {
        /// <summary>
        /// Indicates the transfer is waiting to have an <see cref="EncryptedKeyHeader"/> created
        /// </summary>
        AwaitingTransferKey = 1,
        
        /// <summary>
        /// Item is queued in the outbox and will be send with the next call to ProcessOutbox
        /// </summary>
        Queued = 3,
        
        /// <summary>
        /// Indicates the transfer was successfully delivered and directly written to the target drive or delivered to the inbox
        /// </summary>
        Delivered = 7,
        
        /// <summary>
        /// Recipient server rejected the transfer, client should retry 
        /// </summary>
        [Obsolete("removing wip")]
        TotalRejectionClientShouldRetry = 9,
        
        /// <summary>
        /// Indicates the recipient server returned a security error
        /// </summary>
        [Obsolete("removing wip")]
        RecipientReturnedAccessDenied = 13
    }
}