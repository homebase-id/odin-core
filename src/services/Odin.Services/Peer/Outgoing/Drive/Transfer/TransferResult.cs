namespace Odin.Services.Peer.Outgoing.Drive.Transfer
{
    public enum TransferResult
    {
        Success = 8000,

        /// <summary>
        /// Generic error indicating the recipient's server failed 
        /// </summary>
        RecipientServerError = 500,

        /// <summary>
        /// Thrown we a transfer fails but the reason is not known :)
        /// </summary>
        UnknownError = 800,

        /// <summary>
        /// Indicates the recipient server did not respond to the request (i.e. timeout occured)
        /// </summary>
        RecipientServerNotResponding = 911,

        RecipientServerReturnedAccessDenied = 909,

        /// <summary>
        /// Indicates the file's header has AllowDistribution == false.  The file should be removed from the queue
        /// </summary>
        FileDoesNotAllowDistribution = 1001,

        /// <summary>
        /// Indicates the target recipient does not match the ACL requirements on the file 
        /// </summary>
        RecipientDoesNotHavePermissionToFileAcl = 1002
    }
}