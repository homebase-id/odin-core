using System;
using Dawn;
using Newtonsoft.Json;
using Youverse.Core.Identity;
using Youverse.Core.Identity.DataAttribute;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.Contacts.Circle.Requests
{
    public class ConnectionRequest: ConnectionRequestHeader, IIncomingCertificateMetaData
    {
        [JsonConstructor]
        public ConnectionRequest() { }

        /// <summary>
        /// Individual who sent the invite
        /// </summary>
        public DotYouIdentity SenderDotYouId { get; set; }
        
        /// <summary>
        /// The name to be shown the recipient on the request
        /// </summary>
        public NameAttribute Name { get; set; }
        
        public Int64 ReceivedTimestampMilliseconds { get; set; }

        public string GetSenderDisplayName()
        {
            return $"{SenderDotYouId} ({this.Name.Personal} {this.Name.Surname})";
        }

        public string RSAEncryptedExchangeCredentials { get; set; }

        /// <summary>
        /// The Id to the <see cref="AccessRegistration"/> which will be used to give the recipient access if the recipient accepts the connection request
        /// </summary>
        public Guid PendingAccessRegistrationId { get; set; }

        /// <summary>
        /// Validates this instance has the minimal amount of information to be used.
        /// </summary>
        public virtual void Validate()
        {
            Guard.Argument(SenderDotYouId.ToString(), nameof(SenderDotYouId)).NotEmpty().NotNull();
            // Guard.Argument(SenderPublicKeyCertificate, nameof(SenderPublicKeyCertificate)).NotEmpty().NotNull();
            Guard.Argument(SenderDotYouId.ToString(), nameof(SenderDotYouId)).NotEmpty().NotNull();
            Guard.Argument(Name, nameof(Name)).NotNull();
            Guard.Argument(this.Name.Personal, nameof(this.Name.Personal)).NotEmpty().NotNull();
            Guard.Argument(this.Name.Surname, nameof(this.Name.Surname)).NotEmpty().NotNull();

            Guard.Argument(Recipient.ToString(), nameof(Recipient)).NotEmpty().NotNull();

            Guard.Argument(Id, nameof(Id)).NotEqual(Guid.Empty);
        }

    }
}
