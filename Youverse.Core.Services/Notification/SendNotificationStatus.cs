namespace Youverse.Core.Services.Notification
{
    public enum SendNotificationStatus
    {
        /// <summary>
        /// Indicates the transfer was successfully delivered.
        /// </summary>
        Delivered = 1,

        /// <summary>
        /// Specifies there was a failure to send the transfer and it will be retried.
        /// </summary>
        Failed = 2
    }
}