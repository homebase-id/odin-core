using System;
using DotYou.Types.Circle;

namespace DotYou.Types.Messaging
{
    public class ChatMessageEnvelope : IIncomingCertificateMetaData
    {
        public Guid Id { get; set; }

        public DotYouIdentity Recipient { get; set; }
        
        public string Body { get; set; }
        
        public string SenderPublicKeyCertificate { get; set; }
        
        public DotYouIdentity SenderDotYouId { get; set; }
        
        public long ReceivedTimestamp { get; set; }
    }
}