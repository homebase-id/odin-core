using Dawn;

namespace Youverse.Core.Services.Contacts.Circle.Requests
{
    /// <summary>
    /// Sent when a <see cref="ConnectionRequest"/> is accepted by the <see cref="ConnectionRequest.Recipient"/> 
    /// to establish a connection
    /// </summary>
    public class ConnectionRequestReply
    {
        public string SharedSecretEncryptedCredentials { get; set; }

        public ContactRequestData ContactData { get; set; }

        public string SenderDotYouId { get; set; }

        public long ReceivedTimestampMilliseconds { get; set; }

        public void Validate()
        {
            Guard.Argument(SenderDotYouId.ToString(), nameof(SenderDotYouId)).NotEmpty().NotNull();
            Guard.Argument(ContactData.GivenName, nameof(ContactData.GivenName)).NotEmpty().NotNull();
            Guard.Argument(ContactData.Surname, nameof(ContactData.Surname)).NotEmpty().NotNull();
        }
    }
}