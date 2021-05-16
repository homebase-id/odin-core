using System;
using Dawn;
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
        /// First name of the sender at the time of the invitation
        /// </summary>
        public string SenderGivenName { get; set; }

        /// <summary>
        /// Last name of the sender at the time of the invitation
        /// </summary>
        public string SenderSurname { get; set; }

        public Int64 ReceivedTimestamp { get; set; }

        public string GetSenderDisplayName()
        {
            return $"{SenderDotYouId} ({SenderGivenName} {SenderSurname})";
        }

        /// <summary>
        /// Validates this instance has the minimal amount of information to be used.
        /// </summary>
        public virtual void Validate()
        {
            Guard.Argument(SenderDotYouId.ToString(), nameof(SenderDotYouId)).NotEmpty().NotNull();
            Guard.Argument(SenderPublicKeyCertificate, nameof(SenderPublicKeyCertificate)).NotEmpty().NotNull();
            Guard.Argument(SenderDotYouId.ToString(), nameof(SenderDotYouId)).NotEmpty().NotNull();
            Guard.Argument(SenderGivenName, nameof(SenderGivenName)).NotEmpty().NotNull();
            Guard.Argument(SenderSurname, nameof(SenderSurname)).NotEmpty().NotNull();

            Guard.Argument(Recipient.ToString(), nameof(Recipient)).NotEmpty().NotNull();
            Guard.Argument(Recipient.ToString(), nameof(Recipient)).NotEmpty().NotNull();

            Guard.Argument(Id, nameof(Id)).NotEqual(Guid.Empty);
        }

    }
}
