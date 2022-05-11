using Youverse.Core.Identity;

namespace Youverse.Core.Services.Contacts.Circle.Notification
{
    public class SendNotificationResult
    {
        public DotYouIdentity Recipient { get; set; }

        public SendNotificationStatus Status { get; set; }
    }
}