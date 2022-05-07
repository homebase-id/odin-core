using Dawn;
using Youverse.Core.Identity.DataAttribute;

namespace Youverse.Core.Services.Contacts.Circle.Requests
{
    /// <summary>
    /// Sent when a <see cref="ConnectionRequest"/> is accepted by the <see cref="ConnectionRequest.Recipient"/> 
    /// to establish a connection
    /// </summary>
    public class ConnectionRequestReply
    {
        /// <summary>
        /// The name to be shown the recipient on the request
        /// </summary>
        public NameAttribute Name { get; set; }
        
        public string SharedSecretEncryptedCredentials { get; set; }
        
        public string RecipientGivenName { get; set; }
        
        public string RecipientSurname { get; set; }
        
        public string SenderDotYouId { get; set; }
        
        public long ReceivedTimestampMilliseconds { get; set; }
        
        public void Validate()
        {
            Guard.Argument(SenderDotYouId.ToString(), nameof(SenderDotYouId)).NotEmpty().NotNull();
            Guard.Argument(RecipientGivenName, nameof(RecipientGivenName)).NotEmpty().NotNull();
            Guard.Argument(RecipientSurname, nameof(RecipientSurname)).NotEmpty().NotNull();
        }

    }
}
