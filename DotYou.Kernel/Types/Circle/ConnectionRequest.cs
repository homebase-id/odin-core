using System;
using Dawn;
using DotYou.Types.DataAttribute;
using Newtonsoft.Json;

namespace DotYou.Types.Circle
{
    public class ConnectionRequest: ConnectionRequestHeader, IIncomingCertificateMetaData
    {
        [JsonConstructor]
        public ConnectionRequest() { }

        /// <summary>
        /// Individual who sent the invite
        /// </summary>
        public DotYouIdentity SenderDotYouId { get; set; }
        
        public string SenderPublicKeyCertificate { get; set; }

        /// <summary>
        /// The name to be shown the recipient on the request
        /// </summary>
        public NameAttribute Name { get; set; }
        
        public Int64 ReceivedTimestampMilliseconds { get; set; }

        public string GetSenderDisplayName()
        {
            return $"{SenderDotYouId} ({this.Name.Personal} {this.Name.Surname})";
        }

        /// <summary>
        /// Validates this instance has the minimal amount of information to be used.
        /// </summary>
        public virtual void Validate()
        {
            Guard.Argument(SenderDotYouId.ToString(), nameof(SenderDotYouId)).NotEmpty().NotNull();
            Guard.Argument(SenderPublicKeyCertificate, nameof(SenderPublicKeyCertificate)).NotEmpty().NotNull();
            Guard.Argument(SenderDotYouId.ToString(), nameof(SenderDotYouId)).NotEmpty().NotNull();
            Guard.Argument(Name, nameof(Name)).NotNull();
            Guard.Argument(this.Name.Personal, nameof(this.Name.Personal)).NotEmpty().NotNull();
            Guard.Argument(this.Name.Surname, nameof(this.Name.Surname)).NotEmpty().NotNull();

            Guard.Argument(Recipient.ToString(), nameof(Recipient)).NotEmpty().NotNull();

            Guard.Argument(Id, nameof(Id)).NotEqual(Guid.Empty);
        }

    }
}
