using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core.Cryptography.Data;
using Odin.Core.Identity;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Contacts.Circle.Requests;

namespace Odin.Core.Services.Contacts.Circle.Membership
{
    /// <summary>
    /// Specifies that an identity shares a connection with another identity (i.e. friend request)
    /// </summary>
    public class IdentityConnectionRegistration
    {
        private ConnectionStatus _status;

        public IdentityConnectionRegistration()
        {
            
        }
        public Guid Id
        {
            get { return this.OdinId; }
            set
            {
                //no-op
            }
        }

        public OdinId OdinId { get; init; }
        
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
        /// The Id of the <see cref="ClientAccessToken"/> to be sent when communicating with this OdinId's host
        /// </summary>
        public Guid ClientAccessTokenId { get; set; }

        /// <summary>
        /// The AccessTokenHalfKey of the <see cref="ClientAccessToken"/> to be sent when communicating with this OdinId's host
        /// </summary>
        public byte[] ClientAccessTokenHalfKey { get; set; }
        
        /// <summary>
        /// The SharedSecret of the <see cref="ClientAccessToken"/> used to encrypt payloads when
        /// communicating with this OdinId's host.  This is never sent over the wire.
        /// </summary>
        public byte[] ClientAccessTokenSharedSecret { get; set; } //TODO: this needs to be encrypted when stored; 

        public EncryptedClientAccessToken EncryptedClientAccessToken { get; set; }

        public long LastUpdated { get; set; }
        public long Created { get; set; }

        /// <summary>
        /// The contact data received when the connection was established 
        /// </summary>
        public ContactRequestData OriginalContactData { get; set; }

        public ClientAuthenticationToken CreateClientAuthToken()
        {
            var clientAuthToken = new ClientAuthenticationToken()
            {
                Id = this.ClientAccessTokenId,
                AccessTokenHalfKey = this.ClientAccessTokenHalfKey.ToSensitiveByteArray(),
                ClientTokenType = ClientTokenType.IdentityConnectionRegistration
            };

            return clientAuthToken;
        }
        
        public ClientAccessToken CreateClientAccessToken()
        {
            return new ClientAccessToken()
            {
                Id = this.ClientAccessTokenId,
                AccessTokenHalfKey = this.ClientAccessTokenHalfKey.ToSensitiveByteArray(),
                ClientTokenType = ClientTokenType.IdentityConnectionRegistration,
                SharedSecret = this.ClientAccessTokenSharedSecret.ToSensitiveByteArray()
            };
        }

        /// <summary>
        /// Returns the minimal info needed for external systems using this data.
        /// </summary>
        /// <returns></returns>
        public RedactedIdentityConnectionRegistration Redacted(bool omitContactData = true)
        {
            return new RedactedIdentityConnectionRegistration()
            {
                OdinId = this.OdinId,
                Status = this.Status,
                Created = this.Created,
                LastUpdated = this.LastUpdated,
                OriginalContactData = omitContactData ? null : this.OriginalContactData,
                AccessGrant = this.AccessGrant?.Redacted()
            };
        }

        public IEnumerable<GuidId> GetCircleIds()
        {
            return this.AccessGrant?.CircleGrants?.Values.Select(cg => cg.CircleId) ?? new List<GuidId>();
        }
    }

    public class RedactedIdentityConnectionRegistration
    {
        public OdinId OdinId { get; set; }

        public ConnectionStatus Status { get; set; }

        /// <summary>
        /// The drives and permissions granted to this connection
        /// </summary>
        public RedactedAccessExchangeGrant AccessGrant { get; set; }

        public long Created { get; set; }
        public long LastUpdated { get; set; }
        public ContactRequestData OriginalContactData { get; set; }
    }
}