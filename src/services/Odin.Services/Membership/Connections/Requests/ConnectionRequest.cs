using System;
using System.Text.Json.Serialization;

using Odin.Core.Cryptography.Data;
using Odin.Core.Time;
using Odin.Services.Util;

namespace Odin.Services.Membership.Connections.Requests
{
    public class ConnectionRequest : ConnectionRequestHeader
    {
        [JsonConstructor]
        public ConnectionRequest() { }

        /// <summary>
        /// A sequential guid id used to determine who was first when two introductory requests were sent
        /// </summary>
        public Guid IntroductoryId { get; set; }

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
        public SymmetricKeyEncryptedAes TempEncryptedFeedDriveStorageKey { get; set; }
        
        /// <summary>
        /// A temporary encryption key used during the connection process
        /// </summary>
        public byte[] TempRawKey { get; set; }

        public Guid VerificationRandomCode { get; set; }
        public byte[] VerificationHash { get; set; }

        /// <summary>
        /// Validates this instance has the minimal amount of information to be used.
        /// </summary>
        public virtual void Validate()
        {
            OdinValidationUtils.AssertNotNullOrEmpty(SenderOdinId, nameof(SenderOdinId));
            OdinValidationUtils.AssertNotNull(Recipient, nameof(Recipient));
            OdinValidationUtils.AssertNotEmptyGuid(Id, nameof(Id));
        }
    }
}