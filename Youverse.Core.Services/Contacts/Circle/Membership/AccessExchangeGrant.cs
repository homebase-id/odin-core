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
    }

    public SymmetricKeyEncryptedAes MasterKeyEncryptedKeyStoreKey { get; set; }

    public Dictionary<string, CircleGrant> CircleGrants { get; set; }

    public AccessRegistration AccessRegistration { get; set; }

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