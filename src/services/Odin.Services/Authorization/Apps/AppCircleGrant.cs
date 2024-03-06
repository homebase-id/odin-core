using System.Collections.Generic;
using System.Linq;
using Odin.Core;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;

namespace Odin.Services.Authorization.Apps;

/// <summary>
/// Permissions granted for a given from an app circle
/// </summary>
public sealed class AppCircleGrant
{
    /// <summary>
    /// The app granting the permissions
    /// </summary>
    public GuidId AppId { get; set; }
    
    /// <summary>
    /// The circle for which the permissions are granted via the App
    /// </summary>
    public GuidId CircleId { get; set; }
    
    public PermissionSet PermissionSet { get; set; }
    public List<DriveGrant> KeyStoreKeyEncryptedDriveGrants { get; set; }

    public RedactedAppCircleGrant Redacted()
    {
        return new RedactedAppCircleGrant()
        {
            AppId= this.AppId,
            CircleId = this.CircleId,
            PermissionSet = this.PermissionSet,
            DriveGrants = this.KeyStoreKeyEncryptedDriveGrants.Select(d => d.Redacted()).ToList()
        };
    }
}

public class RedactedAppCircleGrant
{
    public GuidId AppId { get; set; }
    
    public GuidId CircleId { get; set; }
    public PermissionSet PermissionSet { get; set; }
    public List<RedactedDriveGrant> DriveGrants { get; set; }
}