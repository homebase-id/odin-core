using System;
using Youverse.Core.Identity;
using Youverse.Core.Services.Contacts.Circle;

namespace Youverse.Services.Messaging
{
    public class ChatMessageEnvelope : IIncomingCertificateMetaData
    {
        public Guid Id { get; set; }

        public DotYouIdentity Recipient { get; set; }

        public string Body { get; set; }

        /// <summary>
        /// An image message.  The ImageId can be resolved using the MediaService
        /// </summary>
        public Guid MediaId { get; set; }

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