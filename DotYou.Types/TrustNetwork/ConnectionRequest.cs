using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DotYou.Types.TrustNetwork
{
    public class ConnectionRequest
    {
        public ConnectionRequest()
        {
            this.Id = Guid.NewGuid();
        }

        public Guid Id { get; set; }

        /// <summary>
        /// Individual receiving the invite
        /// </summary>
        public DotYouIdentity Recipient { get; set; }

        /// <summary>
        /// Individual who sent the invite
        /// </summary>
        public DotYouIdentity Sender { get; set; }

        /// <summary>
        /// First name of the sender at the time of the invitation
        /// </summary>
        public string SenderGivenName { get; set; }

        /// <summary>
        /// Last name of the sender at the time of the invitation
        /// </summary>
        public string SenderSurname { get; set; }

        /// <summary>
        /// The date the invititation was sent from the <see cref="Sender"/>'s server.
        /// </summary>
        public Int64 DateSent { get; set; }

        /// <summary>
        /// Text to be sent with the invite explaining why you should connect with me.
        /// </summary>
        public string Message { get; set; }

        public string GetSenderDisplayName()
        {
            return $"{Sender} ({SenderGivenName} {SenderSurname})";
        }
    }
}
