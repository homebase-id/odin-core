using System;

namespace DotYou.Types.Messaging
{
    public class ChatMessagePayload
    {
        public Guid Id { get; set; }

        public Contact Sender { get; set; }

        public Contact Recipient { get; set; }

        public string Body { get; set; }

        public Int64 Sent { get; set; }

    }
}