using System;
using System.Collections.Generic;
using System.Linq;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.Contacts.Circle.Membership;

/// <summary>
/// Bundles the exchange grant and access registration given to a single <see cref="IdentityConnectionRegistration"/>
/// </summary>
public class AccessExchangeGrant
{
    //TODO: this is a horrible name.  fix. 
    public AccessExchangeGrant()
    {
        this.CircleGrants = new Dictionary<string, CircleGrant>(StringComparer.Ordinal);
        this.AppGrants = new Dictionary<string, CircleGrant>(StringComparer.Ordinal);
    }

    public SymmetricKeyEncryptedAes MasterKeyEncryptedKeyStoreKey { get; set; }

    /// <summary>
    /// The permissions granted from a given circle.  The key is the circle Id.
    /// </summary>
    public Dictionary<string, CircleGrant> CircleGrants { get; set; }

    /// <summary>
    /// The permissions granted from being with-in a circle that has been authorized by an App.  The Key is the AppId
    /// </summary>
    public Dictionary<string, CircleGrant> AppGrants { get; set; }
    
    public AccessRegistration AccessRegistration { get; set; }

    /// <summary>
    /// if true, revokes access while remaining connected.
    /// </summary>
    public bool IsRevoked { get; set; }

    public bool IsValid()
    {
        return !IsRevoked && !this.AccessRegistration.IsRevoked;
    }

    public RedactedAccessExchangeGrant Redacted()
    {
        return new RedactedAccessExchangeGrant()
        {
            IsRevoked = this.IsRevoked,
            CircleGrants = this.CircleGrants.Values.Select(cg => cg.Redacted()).ToList()
        };
    }
}

public class RedactedAccessExchangeGrant
{
    public bool IsRevoked { get; set; }
    public List<RedactedCircleGrant> CircleGrants { get; set; }
}