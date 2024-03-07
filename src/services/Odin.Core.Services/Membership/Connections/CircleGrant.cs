using System.Collections.Generic;
using System.Linq;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;

namespace Odin.Core.Services.Membership.Connections;

/// <summary>
/// Permissions granted for a given circle
/// </summary>
public class CircleGrant
{
    public CircleGrant()
    {
        
    }
    public GuidId CircleId { get; set; }
    public PermissionSet PermissionSet { get; set; }
    public List<DriveGrant> KeyStoreKeyEncryptedDriveGrants { get; set; }

    public RedactedCircleGrant Redacted()
    {
        return new RedactedCircleGrant()
        {
            CircleId = this.CircleId,
            PermissionSet = this.PermissionSet,
            DriveGrants = this.KeyStoreKeyEncryptedDriveGrants.Select(d => d.Redacted()).ToList()
        };
    }
}

public class RedactedCircleGrant
{
    public GuidId CircleId { get; set; }
    public PermissionSet PermissionSet { get; set; }
    public List<RedactedDriveGrant> DriveGrants { get; set; }
}