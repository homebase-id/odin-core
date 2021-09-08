namespace DotYou.DigitalIdentityHost.Controllers.Perimeter.Xfer
{
    public enum FailureReason
    {
        None = 0,
        
        /// <summary>
        /// Indicates the public key used to encrypt the recipients key was invalid.  In this case, you should retrieve the latest public key  
        /// </summary>
        RecipientPublicKeyInvalid = 1,
        
        /// <summary>
        /// Indicates the payload specified is missing required data.
        /// </summary>
        InvalidPayload = 2,
        
        InternalServerError = 500
    }
}