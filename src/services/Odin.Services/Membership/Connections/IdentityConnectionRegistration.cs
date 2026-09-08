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
        /// When the owner completed the connection review; null means New (never reviewed).
        /// </summary>
        /// <remarks>
        /// Promoted from the <c>Connections.ReviewedAt</c> column, which is its only at-rest home.
        /// <see cref="CircleNetworkStorage"/> maps it into this object on read and back to the column on
        /// write; it is deliberately absent from <c>IcrAccessRecord</c> -- the type that becomes the row's
        /// <c>data</c> blob -- because a second copy in there would let the pagination query (column) and
        /// the hydrated object disagree.  See docs/drive-addressing.md, "One at-rest copy".
        /// </remarks>
        public UnixTimeUtc? ReviewedAt { get; set; }

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
            
            //TODO: CAT - if this is null, we cannot create client access token.

            var cat = EncryptedClientAccessToken.Decrypt(icrDecryptionKey);
            return cat;
        }

        /// <summary>
        /// The shape a third-party viewer gets: the identity and its public contact card, and nothing else.
        /// </summary>
        /// <remarks>
        /// The connections list a peer may see is a list of identities, never a list of the owner's
        /// judgments (docs/connection-defaults.md, "Viewer-scoped redaction").  So everything that records
        /// what the owner thinks or how the relationship came about -- the review, the introducer, the
        /// origin, the grants and their circles -- is absent here, rather than being present-but-empty.
        ///
        /// <para>
        /// Note that <c>omitContactData</c> is the wrong axis for this and always was: it strips the
        /// harmless half (the public card the viewer is there for) and keeps the sensitive half.  This
        /// method is the right axis, and the flag stays only for its owner-side use.
        /// </para>
        /// </remarks>
        public RedactedIdentityConnectionRegistration RedactedForExternalViewer()
        {
            return new RedactedIdentityConnectionRegistration()
            {
                OdinId = this.OdinId,
                OriginalContactData = this.OriginalContactData
            };
        }

        /// <summary>
        /// Returns the minimal info needed for external systems using this data.
        /// </summary>
        /// <remarks>
        /// Owner-side shape: it carries the owner's judgments and must only be served to the owner's own
        /// clients.  Use <see cref="RedactedForExternalViewer"/> for anyone else.
        /// </remarks>
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
                ReviewedAt = this.ReviewedAt
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
        /// When the owner completed the connection review; null means New.  Owner-private -- this shape is
        /// served to the owner's own clients only, never to a peer (docs/connection-defaults.md).
        /// </summary>
        public UnixTimeUtc? ReviewedAt { get; init; }
    }
}