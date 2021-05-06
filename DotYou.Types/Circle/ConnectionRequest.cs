using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DotYou.Types.Circle
{
    
    public class ConnectionRequest: IncomingMessageBase
    {
        public ConnectionRequest()
        {

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

        /// <summary>
        /// Validates this instance has the minimal amount of information to be used.
        /// </summary>
        public virtual void Validate()
        {
            var isInvalid = string.IsNullOrEmpty(this.SenderPublicKeyCertificate)
            || string.IsNullOrWhiteSpace(this.SenderPublicKeyCertificate)
            || string.IsNullOrEmpty(this.Sender)
            || string.IsNullOrWhiteSpace(this.Sender)
            || string.IsNullOrEmpty(this.Recipient)
            || string.IsNullOrWhiteSpace(this.Recipient)
            || this.Id == Guid.Empty
            || string.IsNullOrEmpty(this.SenderGivenName)
            || string.IsNullOrWhiteSpace(this.SenderGivenName);
            
            //TODO: add other checks

            if (isInvalid)
            {
                throw new InvalidDataException("Connection Request is invalid");
            }
        }
    }

    /// <summary>
    /// Base class for requests incoming from other digital identities
    /// </summary>
    public abstract class IncomingMessageBase
    {
        private string senderKey;
        
        /// <summary>
        /// Specifies the pubilc key certificate of the <see cref="DotYouIdentity"/> who sent this message
        /// </summary>
        public string SenderPublicKeyCertificate
        {
            get
            {
                return senderKey;
            }
            set
            {
                AssertClassCanSetValue();
                senderKey = value;
            }
        }

        private void AssertClassCanSetValue()
        {
            //HACK: i cannot think of a better way to ensure
            //this value is only set at the right time
            
            //check if the call is coming from the incoming namespace

            string ns = "DotYou.TenantHost.Controllers.Incoming";
            
            var stack = new StackTrace();
            var namespaceAuthorized = stack.GetFrames().Any(frame => frame.GetMethod() != null
                                           && frame.GetMethod().DeclaringType.FullName.StartsWith(ns));

            if (!namespaceAuthorized)
            {
                throw new InvalidOperationException(
                    $"You can only set this MessageSenderInfoBase from the classes in the namespace [{ns}]");
            }
        }
    }
}
