using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Transit
{
    public enum TransferStatus
    {
        /// <summary>
        /// Indicates the transfer is waiting to have an <see cref="EncryptedKeyHeader"/> created
        /// </summary>
        AwaitingTransferKey = 1,
        
        /// <summary>
        /// 
        /// </summary>
        TransferKeyCreated = 3,

        /// <summary>
        /// Indicates the transfer was successfully delivered.
        /// </summary>
        Delivered = 5,
        
        /// <summary>
        /// Specifies there was a failure to send the transfer and it will be retried.
        /// </summary>
        PendingRetry = 8,
        
        /// <summary>
        /// Recipient server rejected the transfer, client should retry 
        /// </summary>
        TotalRejectionClientShouldRetry = 9,
        
        /// <summary>
        /// Indicates the file is not allowed to be sent (i.e. AllowDistribution is false)
        /// </summary>
        FileDoesNotAllowDistribution = 11
    }
}