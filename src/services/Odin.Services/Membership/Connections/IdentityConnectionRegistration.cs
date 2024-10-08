using System;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Services.Membership.Connections
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
        /// The encrypted <see cref="ClientAccessToken"/> token used when accessing another connected identity
        /// </summary>
        public EncryptedClientAccessToken EncryptedClientAccessToken { get; set; }
        
        public long LastUpdated { get; set; }
        public long Created { get; set; }

        /// <summary>
        /// The contact data received when the connection was established 
        /// </summary>
        public ContactRequestData OriginalContactData { get; set; }

        public ClientAuthenticationToken CreateClientAuthToken(SensitiveByteArray icrDecryptionKey)
        {
            return this.CreateClientAccessToken(icrDecryptionKey).ToAuthenticationToken();
        }

        public ClientAccessToken CreateClientAccessToken(SensitiveByteArray icrDecryptionKey)
        {
            var cat = EncryptedClientAccessToken.Decrypt(icrDecryptionKey);
            return cat;
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