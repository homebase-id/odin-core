using System;
using System.Text.Json.Serialization;
using Dawn;
using Youverse.Core.Services.Contacts.Circle.Membership;

namespace Youverse.Core.Services.Contacts.Circle.Requests
{
    public class ConnectionRequest : ConnectionRequestHeader
    {
        [JsonConstructor]
        public ConnectionRequest()
        {
        }

        /// <summary>
        /// Individual who sent the invite
        /// </summary>
        // public DotYouIdentity SenderDotYouId { get; set; }
        public string SenderDotYouId { get; set; }
        
        public Int64 ReceivedTimestampMilliseconds { get; set; }

        public string RSAEncryptedExchangeCredentials { get; set; }

        /// <summary>
        /// The exchange grant which will be given to the recipient if the connection request is accepted
        /// </summary>
        public AccessExchangeGrant PendingAccessExchangeGrant { get; set; }
        
        /// <summary>
        /// Validates this instance has the minimal amount of information to be used.
        /// </summary>
        public virtual void Validate()
        {
            Guard.Argument(SenderDotYouId.ToString(), nameof(SenderDotYouId)).NotEmpty().NotNull();
            // Guard.Argument(SenderPublicKeyCertificate, nameof(SenderPublicKeyCertificate)).NotEmpty().NotNull();
            Guard.Argument(SenderDotYouId.ToString(), nameof(SenderDotYouId)).NotEmpty().NotNull();
            // Guard.Argument(Name, nameof(Name)).NotNull();
            // Guard.Argument(this.Name.Personal, nameof(this.Name.Personal)).NotEmpty().NotNull();
            // Guard.Argument(this.Name.Surname, nameof(this.Name.Surname)).NotEmpty().NotNull();

            Guard.Argument(Recipient.ToString(), nameof(Recipient)).NotEmpty().NotNull();

            Guard.Argument(Id, nameof(Id)).NotEqual(Guid.Empty);
        }
    }
}