using System;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Membership.Circles;
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

        public bool IsConfirmedConnection()
        {
            return PeerKeyStore?.CircleGrants.TryGetValue(SystemCircleConstants.ConfirmedConnectionsCircleId, out _) ?? false;
        }

        /// <summary>
        /// True when the owner has completed the connection review.  This is the owner's own recorded
        /// act - it is never sent to the peer and never derived from grants.
        /// </summary>
        public bool IsReviewed()
        {
            return this.ReviewedAt.HasValue;
        }

        /// <summary>
        /// Records that the owner reviewed this connection.  Idempotent, and never moves an existing
        /// stamp: the first review is the one that happened.
        /// </summary>
        /// <remarks>
        /// In-memory only - the caller is expected to persist the registration.  Use
        /// <c>CircleNetworkService.StampReviewedAsync</c> when the stamp is the only thing being written.
        /// </remarks>
        public void MarkReviewed(UnixTimeUtc? reviewedAt = null)
        {
            this.ReviewedAt ??= reviewedAt ?? UnixTimeUtc.Now();
        }

        /// <summary>
        /// The drives and permissions granted to this connection
        /// </summary>
        [JsonPropertyName("accessGrant")]
        public PeerKeyStore PeerKeyStore { get; set; }

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
        /// the encryption of the <see cref="PeerKeyStore"/> MasterKeyEncryptedKeyStoreKey
        /// </summary>
        public EccEncryptedPayload TempWeakKeyStoreKey { get; set; }

        public UnixTimeUtc LastUpdated { get; set; }
        public UnixTimeUtc Created { get; set; }

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

        /// <summary>
        /// When the owner completed the connection review; null means the connection has never been
        /// reviewed ("New").
        /// <para>
        /// This lives in the <c>Connections.ReviewedAt</c> column, not in the ICR data blob.  The column
        /// is the only at-rest home: <see cref="CircleNetworkStorage"/> maps it in on read and back out on
        /// write, and it is deliberately absent from <c>IcrAccessRecord</c> so a naive re-serialize cannot
        /// mint a second copy that drifts from the column the contact book pages on.
        /// </para>
        /// </summary>
        [JsonIgnore]
        public UnixTimeUtc? ReviewedAt { get; set; }

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
            
            //TODO: CAT - if this is null, we cannot create client access token.

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
                AccessGrant = this.PeerKeyStore?.Redacted(),
                Rku = EncryptedClientAccessToken == null,
                HasVerificationHash = !this.VerificationHash.IsNullOrEmpty(),
                ReviewedAt = this.ReviewedAt,
                Vetted = this.IsConnected() && this.IsReviewed()
            };
        }

        /// <summary>
        /// The shape served to third parties (guest callers, and anonymous viewers where the tenant
        /// settings permit it): the identity and its public contact card, nothing else.
        /// </summary>
        /// <remarks>
        /// The connections list a peer may see is a list of identities, never a list of my judgments.
        /// Everything the owner recorded about this contact -- the review stamp, who introduced them,
        /// how the connection originated, what they were granted -- is owner-private and is dropped
        /// here.  The tenant setting decides *whether* a third party sees the list; this decides *what*
        /// they see.
        /// </remarks>
        public RedactedIdentityConnectionRegistration RedactedForThirdParty()
        {
            return new RedactedIdentityConnectionRegistration()
            {
                OdinId = this.OdinId,
                Status = this.Status,
                OriginalContactData = this.OriginalContactData
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
        public RedactedPeerKeyStore AccessGrant { get; init; }

        public UnixTimeUtc Created { get; set; }
        public UnixTimeUtc LastUpdated { get; set; }
        public ContactRequestData OriginalContactData { get; init; }
        public OdinId? IntroducerOdinId { get; init; }
        public ConnectionRequestOrigin ConnectionRequestOrigin { get; init; }

        public bool HasVerificationHash { get; init; }

        public bool Rku { get; init; }

        /// <summary>
        /// When the owner completed the connection review; null means never reviewed ("New").
        /// Owner/app viewers only -- always null on the third-party shape.
        /// </summary>
        public UnixTimeUtc? ReviewedAt { get; init; }

        /// <summary>
        /// True if the identity is connected and the owner has completed the review.
        /// </summary>
        /// <remarks>
        /// Legacy name, kept so V1 clients keep working through the transition; it is now served as
        /// <c>ReviewedAt != null</c> rather than Confirmed Connections membership.  New clients should
        /// read <see cref="ReviewedAt"/>.
        /// </remarks>
        public bool Vetted { get; init; }
    }
}