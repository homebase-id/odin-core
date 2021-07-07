using System;
using DotYou.Types.Circle;

namespace DotYou.Types.Messaging
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
    
    public class ChatMessageEnvelope : IIncomingCertificateMetaData
    {
        public Guid Id { get; set; }

        public DotYouIdentity Recipient { get; set; }

        public string Body { get; set; }

        /// <summary>
        /// Timestamp indicating when the message was transmitted
        /// This should be set on the Sender's Digital Identity 
        /// </summary>
        public long SentTimestampMilliseconds { get; set; }

        public string SenderPublicKeyCertificate { get; set; }

        public DotYouIdentity SenderDotYouId { get; set; }

        public long ReceivedTimestampMilliseconds { get; set; }
    }
}