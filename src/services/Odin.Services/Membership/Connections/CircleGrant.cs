using System.Collections.Generic;
using System.Linq;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;

namespace Odin.Services.Membership.Connections;

/// <summary>
/// Permissions granted for a given circle
/// </summary>
public class CircleGrant
{
    public CircleGrant() { }
    
    public GuidId CircleId { get; set; }
    
    public PermissionSet PermissionSet { get; set; }
    
    public List<DriveGrant> KeyStoreKeyEncryptedDriveGrants { get; set; }
    
    public SymmetricKeyEncryptedAes KeyStoreKeyEncryptedIcrKey { get; init; }
    
    public RedactedCircleGrant Redacted()
    {
        return new RedactedCircleGrant()
        {
            CircleId = this.CircleId,
            PermissionSet = this.PermissionSet,
            DriveGrants = this.KeyStoreKeyEncryptedDriveGrants.Select(d => d.Redacted()).ToList(),
            HasIcrKey = this.KeyStoreKeyEncryptedIcrKey != null
        };
    }
}

public class RedactedCircleGrant
{
    public GuidId CircleId { get; set; }
    public PermissionSet PermissionSet { get; set; }
    public List<RedactedDriveGrant> DriveGrants { get; set; }
    public bool HasIcrKey { get; set; }
}