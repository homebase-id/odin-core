using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core.Cryptography.Data;
using Odin.Core.Services.Authorization.Apps;
using Odin.Core.Services.Authorization.ExchangeGrants;

namespace Odin.Core.Services.Contacts.Circle.Membership;

/// <summary>
/// Bundles the exchange grant and access registration given to a single <see cref="IdentityConnectionRegistration"/>
/// </summary>
public class AccessExchangeGrant
{
    //TODO: this is a horrible name.  fix. 
    public AccessExchangeGrant()
    {
        this.CircleGrants = new();
        this.AppGrants = new();
    }

    public SymmetricKeyEncryptedAes MasterKeyEncryptedKeyStoreKey { get; set; }

    /// <summary>
    /// The permissions granted from a given circle.  The key is the circle Id.
    /// </summary>
    public Dictionary<Guid, CircleGrant> CircleGrants { get; set; }

    /// <summary>
    /// The permissions granted from being with-in a circle that has been authorized by an App.  The main key is the AppId
    /// </summary>
    public Dictionary<Guid, Dictionary<Guid, AppCircleGrant>> AppGrants { get; set; }

    public AccessRegistration AccessRegistration { get; set; }

    /// <summary>
    /// if true, revokes access while remaining connected.
    /// </summary>
    public bool IsRevoked { get; set; }

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
        return !IsRevoked && !this.AccessRegistration.IsRevoked;
    }

    public RedactedAccessExchangeGrant Redacted()
    {
        return new RedactedAccessExchangeGrant()
        {
            IsRevoked = this.IsRevoked,
            CircleGrants = this.CircleGrants.Values.Select(cg => cg.Redacted()).ToList(),
            AppGrants = this.AppGrants.ToDictionary(k => k.Key, pair => pair.Value.Values.Select(v => v.Redacted()))
        };
    }
}

public class RedactedAccessExchangeGrant
{
    public bool IsRevoked { get; set; }
    public List<RedactedCircleGrant> CircleGrants { get; set; }
    public Dictionary<Guid, IEnumerable<RedactedAppCircleGrant>> AppGrants { get; set; }
}