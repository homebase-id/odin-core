using System;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrantRedux;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.Contacts.Circle.Membership
{
    /// <summary>
    /// Bundles the exchange grant and access registration given to a single <see cref="IdentityConnectionRegistration"/>
    /// </summary>
    public class AccessExchangeGrant
    {
        //TODO: this is a horrible name.  fix

        public IExchangeGrant Grant { get; set; }
        public AccessRegistration AccessRegistration { get; set; }

    }
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
        public AccessExchangeGrant Grant { get; set; }

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

        public ClientAuthenticationToken CreateClientAuthToken()
        {
            var clientAuthToken = new ClientAuthenticationToken()
            {
                Id = this.ClientAccessTokenId,
                AccessTokenHalfKey = this.ClientAccessTokenHalfKey.ToSensitiveByteArray()
            };
            
            return clientAuthToken;
        }
    }
}