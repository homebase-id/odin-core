namespace Youverse.Core.Services.Transit
{
    public enum TransferFailureReason
    {
        
        /// <summary>
        /// Could not get the recipient's public key from their DI server. 
        /// </summary>
        TransitPublicKeyInvalid = 5,
        
        /// <summary>
        /// Indicates the recipients public key was rejected when transfer the data.
        /// </summary>
        RecipientPublicKeyInvalid = 10,
        
        /// <summary>
        /// Failed to encrypt data before sending to recipient
        /// </summary>
        CouldNotEncrypt = 400,
        
        /// <summary>
        /// Generic error indicating the recipient's server failed 
        /// </summary>
        RecipientServerError = 500,
        
        /// <summary>
        /// Indicates there was not an encrypted transfer key available in the cache.
        /// </summary>
        EncryptedTransferInstructionSetNotAvailable = 700,
        
        /// <summary>
        /// Thrown we a transfer fails but the reason is not known :)
        /// </summary>
        UnknownError = 800,
        
    }
}