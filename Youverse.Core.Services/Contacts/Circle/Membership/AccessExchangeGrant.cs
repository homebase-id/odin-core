using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.Contacts.Circle.Membership;

/// <summary>
/// Bundles the exchange grant and access registration given to a single <see cref="IdentityConnectionRegistration"/>
/// </summary>
public class AccessExchangeGrant
{
    //TODO: this is a horrible name.  fix. 
    //TODO: the structure sucks too; fix

    public IExchangeGrant Grant { get; set; }
    
    public AccessRegistration AccessRegistration { get; set; }

}