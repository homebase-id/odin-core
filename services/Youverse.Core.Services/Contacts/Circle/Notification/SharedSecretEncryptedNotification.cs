namespace Youverse.Core.Services.Contacts.Circle.Notification
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