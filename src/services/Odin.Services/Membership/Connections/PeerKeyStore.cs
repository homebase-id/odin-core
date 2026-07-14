using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Odin.Core.Cryptography.Data;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Services.Membership.Connections;

/// <summary>
/// Bundles the exchange grant and access registration given to a single <see cref="IdentityConnectionRegistration"/>
/// </summary>
public class PeerKeyStore
{
    [JsonPropertyName("masterKeyEncryptedKeyStoreKey")]
    public SymmetricKeyEncryptedAes MasterKeyEncryptedPeerKey { get; set; }

    /// <summary>
    /// The permissions granted from a given circle.  The key is the circle Id.
    /// </summary>
    public Dictionary<Guid, CircleGrant> CircleGrants { get; set; } = new();

    /// <summary>
    /// The permissions granted from being with-in a circle that has been authorized by an App.  The main key is the AppId.  The second key is the CircleId
    /// </summary>
    public Dictionary<Guid, Dictionary<Guid, AppCircleGrant>> AppGrants { get; set; } = new();

    [JsonPropertyName("accessRegistration")]
    public ServerHalfOfClientKey PeerClientKey { get; set; }

    /// <summary>
    /// if true, revokes access while remaining connected.
    /// </summary>
    public bool IsRevoked { get; set; }

    /// <summary>
    /// The store's write-without-read keypair: public key in clear, private key escrowed
    /// under this store's key store key (the Peer Key). Lets a caller without the Peer Key
    /// (e.g. an app) deposit grants it cannot read back. Null on stores created before this
    /// existed; provisioned the next time the owner grants a circle on this connection.
    /// </summary>
    public EccFullKeyData WriteOnlyKeyPair { get; set; }

    /// <summary>
    /// Grants deposited via <see cref="WriteOnlyKeyPair"/>, awaiting conversion into normal
    /// Peer-Key-encrypted circle grants when the key store key is next in scope.
    /// </summary>
    public List<DepositedGrant> DepositedGrants { get; set; } = new();

    [JsonIgnore]
    public bool HasPendingDeposits => DepositedGrants?.Count > 0;

    public void AddUpdateAppCircleGrant(AppCircleGrant appCircleGrant)
    {
        var appKey = appCircleGrant.AppId;
        if (!this.AppGrants.Remove(appKey, out var appCircleGrantsDictionary))
        {
            appCircleGrantsDictionary = new();
        }

        appCircleGrantsDictionary[appCircleGrant.CircleId] = appCircleGrant;
        this.AppGrants[appKey] = appCircleGrantsDictionary;
    }

    public bool IsValid()
    {
        return !IsRevoked && !this.PeerClientKey.IsRevoked;
    }

    public RedactedPeerKeyStore Redacted()
    {
        return new RedactedPeerKeyStore()
        {
            IsRevoked = this.IsRevoked,
            CircleGrants = this.CircleGrants.Values.Select(cg => cg.Redacted()).ToList(),
            AppGrants = this.AppGrants.ToDictionary(k => k.Key, pair => pair.Value.Values.Select(v => v.Redacted())),
            PendingCircleIds = this.DepositedGrants.Select(d => d.CircleId.Value).ToList()
        };
    }

    public bool RequiresMasterKeyEncryptionUpgrade()
    {
        return MasterKeyEncryptedPeerKey == null;
    }
}

public class RedactedPeerKeyStore
{
    public bool IsRevoked { get; set; }
    public List<RedactedCircleGrant> CircleGrants { get; set; }
    public Dictionary<Guid, IEnumerable<RedactedAppCircleGrant>> AppGrants { get; set; }

    /// <summary>
    /// Circles deposited via an app's write-only grant but not yet converted into a real
    /// <see cref="CircleGrant"/> — the identity is not yet a member of these, but will be as
    /// soon as the Peer Key is next in scope (owner grant touch or peer CAT auth).
    /// </summary>
    public List<Guid> PendingCircleIds { get; set; }
}