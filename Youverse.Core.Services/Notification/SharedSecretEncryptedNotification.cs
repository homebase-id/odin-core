namespace Youverse.Core.Services.Notification
{
    public class SharedSecretEncryptedNotification
    {
        
        public byte[] InitializationVector { get; set; }
        
        /// <summary>
        /// The encrypted payload
        /// </summary>
        public byte[] Data { get; set; }
    }
}