using System;
using System.Text.Json.Serialization;
using Dawn;
using Odin.Core.Cryptography.Data;
using Odin.Core.Time;

namespace Odin.Core.Services.Membership.Connections.Requests
{
    public class ConnectionRequest : ConnectionRequestHeader
    {
        [JsonConstructor]
        public ConnectionRequest() { }

        /// <summary>
        /// Individual who sent the invite
        /// </summary>
        public string SenderOdinId { get; set; }

        public UnixTimeUtc ReceivedTimestampMilliseconds { get; set; }

        public string ClientAccessToken64 { get; set; }

        /// <summary>
        /// The exchange grant which will be given to the recipient if the connection request is accepted
        /// </summary>
        public AccessExchangeGrant PendingAccessExchangeGrant { get; set; }

        public SymmetricKeyEncryptedAes TempEncryptedIcrKey { get; set; }
        
        /// <summary>
        /// A temporary encryption key used during the connection process
        /// </summary>
        public byte[] TempRawKey { get; set; }

        /// <summary>
        /// Validates this instance has the minimal amount of information to be used.
        /// </summary>
        public virtual void Validate()
        {
            Guard.Argument(SenderOdinId, nameof(SenderOdinId)).NotEmpty().NotNull();
            Guard.Argument(Recipient, nameof(Recipient)).NotEmpty().NotNull();
            Guard.Argument(Id, nameof(Id)).NotEqual(Guid.Empty);
            Guard.Argument(ContactData, nameof(ContactData)).NotNull();

            ContactData.Validate();
        }
    }
}