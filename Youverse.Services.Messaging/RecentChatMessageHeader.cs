using Youverse.Core.Identity;

namespace Youverse.Services.Messaging
{
    /// <summary>
    /// Specifies the most recent message for a given recipient.  Used for dashboarding and the recent messages list
    /// </summary>
    public class RecentChatMessageHeader
    {
        /// <summary>
        /// This is the Id of the other individual or group.  This is not the dotYoudId owner of this instance
        /// </summary>
        public DotYouIdentity DotYouId { get; set; }

        public string Body { get; set; }

        /// <summary>
        /// The time to display; whether sent or received
        /// </summary>
        public long Timestamp { get; set; }
    }
}