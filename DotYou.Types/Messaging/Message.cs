using System;
using System.Collections.Generic;
using DotYou.Types.Circle;

namespace DotYou.Types.Messaging
{
    public class Message : IRequireSenderCertificate
    {
        public Guid Id { get; set; }

        public string Topic { get; set; }
        
        public IEnumerable<DotYouIdentity> Recipients { get; set; }

        public IEnumerable<string> Tags { get; set; }

        public Int64 Received { get; set; }

        public string Body { get; set; }
        public string SenderPublicKeyCertificate { get; set; }
        public DotYouIdentity SenderDotYouId { get; set; }
    }
}