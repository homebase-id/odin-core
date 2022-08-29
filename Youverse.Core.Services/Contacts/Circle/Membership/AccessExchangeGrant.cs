using System;
using System.Collections.Generic;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;

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
}

/// <summary>
/// Permissions granted for a given circle
/// </summary>
public class CircleGrant
{
    public ByteArrayId CircleId { get; set; }
    public List<DriveGrant> KeyStoreKeyEncryptedDriveGrants { get; set; }
    public PermissionSet PermissionSet { get; set; }
}