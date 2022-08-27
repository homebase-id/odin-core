using System;
using System.Collections.Generic;
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
        this.CircleGrants = new Dictionary<string, ExchangeGrant>(StringComparer.Ordinal);
    }

    public Dictionary<string, ExchangeGrant> CircleGrants { get; set; }

    [Obsolete("TODO: need to read from circles")]
    public ExchangeGrant Grant { get; set; }

    public AccessRegistration AccessRegistration { get; set; }
}