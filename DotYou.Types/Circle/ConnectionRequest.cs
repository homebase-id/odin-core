using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace DotYou.Types.Circle
{
    public class ConnectionRequestHeader
    {
        [JsonConstructor]
        public ConnectionRequestHeader() { }
        
        
        public Guid Id { get; set; }
        
        /// <summary>
        /// Individual receiving the invite
        /// </summary>
        public DotYouIdentity Recipient { get; set; }
        
        /// <summary>
        /// Text to be sent with the invite explaining why you should connect with me.
        /// </summary>
        public string Message { get; set; }
        
    }
    
    public class ConnectionRequest: ConnectionRequestHeader, IRequireSenderCertificate
    {
        [JsonConstructor]
        public ConnectionRequest() { }

        // public ConnectionRequest(string senderPublicKey)
        // {
        //     this.SenderPublicKeyCertificate
        // }


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

        public string SenderPublicKeyCertificate { get; set; }
    }
}
