using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Contacts.Circle.Membership
{
    /// <summary>
    /// Specifies that an identity shares a connection with another identity (i.e. friend request)
    /// </summary>
    public class IdentityConnectionRegistration : DotYouIdBase
    {
        private ConnectionStatus _status;

        public ConnectionStatus Status
        {
            get { return _status; }
            set
            {
                _status = value;
                this.LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
        }

        public bool IsConnected()
        {
            return this._status == ConnectionStatus.Connected;
        }

        /// <summary>
        /// The drives and permissions granted to this connection
        /// </summary>
        public AccessExchangeGrant AccessGrant { get; set; }

        /// <summary>
        /// The Id of the <see cref="ClientAccessToken"/> to be sent when communicating with this DotYouId's host
        /// </summary>
        public Guid ClientAccessTokenId { get; set; }

        /// <summary>
        /// The AccessTokenHalfKey of the <see cref="ClientAccessToken"/> to be sent when communicating with this DotYouId's host
        /// </summary>
        public byte[] ClientAccessTokenHalfKey { get; set; }

        /// <summary>
        /// The SharedSecret of the <see cref="ClientAccessToken"/> used to encrypt payloads when
        /// communicating with this DotYouId's host.  This is never sent over the wire.
        /// </summary>
        public byte[] ClientAccessTokenSharedSecret { get; set; } //TODO: this needs to be encrypted when stored; 

        public long LastUpdated { get; set; }
        public long Created { get; set; }

        public ClientAuthenticationToken CreateClientAuthToken()
        {
            var clientAuthToken = new ClientAuthenticationToken()
            {
                Id = this.ClientAccessTokenId,
                AccessTokenHalfKey = this.ClientAccessTokenHalfKey.ToSensitiveByteArray()
            };

            return clientAuthToken;
        }

        /// <summary>
        /// Returns the minimal info needed for external systems using this data.
        /// </summary>
        /// <returns></returns>
        public RedactedIdentityConnectionRegistration Redacted()
        {
            return new RedactedIdentityConnectionRegistration()
            {
                Status = this.Status,
                Created = this.Created,
                LastUpdated = this.LastUpdated,
                AccessGrant = this.AccessGrant.Redacted()
            };
        }
    }

    public class RedactedIdentityConnectionRegistration
    {
        public ConnectionStatus Status { get; set; }

        /// <summary>
        /// The drives and permissions granted to this connection
        /// </summary>
        public RedactedAccessExchangeGrant AccessGrant { get; set; }

        // access exchange grant isRevoked
        // accessRegistration isRevoked
        // accessRegistration created
        public long Created { get; set; }
        public long LastUpdated { get; set; }
    }
}