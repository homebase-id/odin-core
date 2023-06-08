﻿using Dawn;

namespace Odin.Core.Services.Contacts.Circle.Requests
{
    /// <summary>
    /// Sent when a <see cref="ConnectionRequest"/> is accepted by the <see cref="ConnectionRequest.Recipient"/>
    /// to establish a connection
    /// </summary>
    public class ConnectionRequestReply
    {
        public string SharedSecretEncryptedCredentials { get; set; }

        public ContactRequestData ContactData { get; set; }

        public string SenderOdinId { get; set; }

        public long ReceivedTimestampMilliseconds { get; set; }

        public void Validate()
        {
            Guard.Argument(SenderOdinId.ToString(), nameof(SenderOdinId)).NotEmpty().NotNull();
            Guard.Argument(ContactData.Name, nameof(ContactData.Name)).NotEmpty().NotNull();
        }
    }
}
