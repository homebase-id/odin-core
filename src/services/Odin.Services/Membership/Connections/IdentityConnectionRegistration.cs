using System;
using System.Diagnostics;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Services.Membership.Connections
{
    /// <summary>
    /// Specifies that an identity shares a connection with another identity (i.e. friend request)
    /// </summary>
    [DebuggerDisplay("{OdinId.DomainName} with Status {Status}")]
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

        /// <summary>
        /// Temporary storage for the CAT until the ICR key is available to encrypt it
        /// </summary>
        public EccEncryptedPayload TemporaryWeakClientAccessToken { get; set; }

        /// <summary>
        /// Storage of the KeyStoreKey until the master key is available to finalize
        /// the encryption of the <see cref="AccessExchangeGrant"/> MasterKeyEncryptedKeyStoreKey
        /// </summary>
        public EccEncryptedPayload TempWeakKeyStoreKey { get; set; }

        public long LastUpdated { get; set; }
        public long Created { get; set; }

        /// <summary>
        /// The contact data received when the connection was established 
        /// </summary>
        public ContactRequestData OriginalContactData { get; set; }

        /// <summary>
        /// How this connection was made
        /// </summary>
        public ConnectionRequestOrigin ConnectionRequestOrigin { get; init; }

        /// <summary>
        /// Nullable, the identity that introduce you to this <see cref="OdinId"/>
        /// </summary>
        public OdinId? IntroducerOdinId { get; init; }

        /// <summary>
        /// A hash generated when the connection is established based a random code and the shared secret
        /// </summary>
        public byte[] VerificationHash { get; set; }

        public ClientAuthenticationToken CreateClientAuthToken(SensitiveByteArray icrDecryptionKey)
        {
            return this.CreateClientAccessToken(icrDecryptionKey).ToAuthenticationToken();
        }

        public ClientAccessToken CreateClientAccessToken(SensitiveByteArray icrDecryptionKey)
        {
            if (null == icrDecryptionKey)
            {
                throw new OdinSecurityException("missing icr key");
            }

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
                IntroducerOdinId = this.IntroducerOdinId,
                ConnectionRequestOrigin = this.ConnectionRequestOrigin,
                AccessGrant = this.AccessGrant?.Redacted(),
                Rku = EncryptedClientAccessToken == null,
                HasVerificationHash = !this.VerificationHash.IsNullOrEmpty()
            };
        }
    }

    public class RedactedIdentityConnectionRegistration
    {
        public OdinId OdinId { get; init; }

        public ConnectionStatus Status { get; init; }

        /// <summary>
        /// The drives and permissions granted to this connection
        /// </summary>
        public RedactedAccessExchangeGrant AccessGrant { get; init; }

        public long Created { get; set; }
        public long LastUpdated { get; set; }
        public ContactRequestData OriginalContactData { get; init; }
        public OdinId? IntroducerOdinId { get; init; }
        public ConnectionRequestOrigin ConnectionRequestOrigin { get; init; }

        public bool HasVerificationHash { get; init; }

        public bool Rku { get; init; }
    }
}