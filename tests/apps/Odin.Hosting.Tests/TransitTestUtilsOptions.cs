namespace Odin.Hosting.Tests
{
    public class TransitTestUtilsOptions
    {
        public static TransitTestUtilsOptions Default = new TransitTestUtilsOptions()
        {
            DisconnectIdentitiesAfterTransfer = true
        };

        public bool DriveAllowAnonymousReads { get; set; } = false;

        public bool DisconnectIdentitiesAfterTransfer { get; set; } = true;

        /// <summary>
        /// Indicates if the process outbox endpoint should be called after sending a transfer
        /// </summary>
        public bool ProcessOutbox { get; set; }

        /// <summary>
        /// Indicates if the process incoming transfers should be called after sending a transfer
        /// </summary>
        public bool ProcessInboxBox { get; set; }
        
        /// <summary>
        /// The data to be uploaded as the payload
        /// </summary>
        public string PayloadData { get; set; }

        public bool EncryptPayload { get; set; } = true;

        public bool IncludeThumbnail { get; set; }
    }
}