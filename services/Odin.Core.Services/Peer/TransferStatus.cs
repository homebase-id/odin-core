using Odin.Core.Services.Transit.Encryption;

namespace Odin.Core.Services.Transit
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
        /// Indicates the transfer was successfully delivered to the inbox.
        /// </summary>
        DeliveredToInbox = 5,
        
        /// <summary>
        /// Indicates the transfer was successfully delivered and directly written to the target drive
        /// </summary>
        DeliveredToTargetDrive = 7,
        
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
        FileDoesNotAllowDistribution = 11,
        
        /// <summary>
        /// Indicates the recipient server returned a security error
        /// </summary>
        RecipientReturnedAccessDenied = 13
    }
}