using System;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.Peer
{
    public enum TransferStatus
    {
        /// <summary>
        /// Indicates we failed to enqueue the item into the outbox.  the client must retry
        /// </summary>
        FailedToEnqueueOutbox = 1,
        
        /// <summary>
        /// Item is queued in the outbox and will be send with the next call to ProcessOutbox
        /// </summary>
        Queued = 3,
        
        /// <summary>
        /// Indicates the transfer was successfully delivered and directly written to the target drive or delivered to the inbox
        /// </summary>
        Delivered = 7,

    }
}