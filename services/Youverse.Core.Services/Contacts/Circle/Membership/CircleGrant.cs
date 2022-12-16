using System.Collections.Generic;
using System.Linq;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;

namespace Youverse.Core.Services.Contacts.Circle.Membership;

/// <summary>
/// Permissions granted for a given circle
/// </summary>
public class CircleGrant
{
    public CircleGrant()
    {
        
    }
    public GuidId CircleId { get; set; }
    public PermissionKeySet PermissionKeySet { get; set; }
    public List<DriveGrant> KeyStoreKeyEncryptedDriveGrants { get; set; }

    public RedactedCircleGrant Redacted()
    {
        return new RedactedCircleGrant()
        {
            CircleId = this.CircleId,
            PermissionKeySet = this.PermissionKeySet,
            DriveGrants = this.KeyStoreKeyEncryptedDriveGrants.Select(d => d.Redacted()).ToList()
        };
    }
}

public class RedactedCircleGrant
{
    public GuidId CircleId { get; set; }
    public PermissionKeySet PermissionKeySet { get; set; }
    public List<RedactedDriveGrant> DriveGrants { get; set; }
}