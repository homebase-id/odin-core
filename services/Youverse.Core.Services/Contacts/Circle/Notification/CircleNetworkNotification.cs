using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Contacts.Circle.Notification
{
    public class CircleNetworkNotification
    {
        public SystemApi TargetSystemApi { get; set; }
        
        /// <summary>
        /// An Id specific to the <see cref="TargetSystemApi"/>
        /// </summary>
        public int NotificationId { get; set; }
    }
}