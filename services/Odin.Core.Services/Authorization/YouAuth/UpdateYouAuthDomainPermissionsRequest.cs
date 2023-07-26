using System.Collections.Generic;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Util;

namespace Odin.Core.Services.Authorization.YouAuth;

public class UpdateYouAuthDomainPermissionsRequest
{
    public AsciiDomainName Domain { get; set; }

    /// <summary>
    /// Permissions to be granted to this app
    /// </summary>
    public PermissionSet PermissionSet { get; set; }

    /// <summary>
    /// The list of drives of which this app should receive access
    /// </summary>
    public IEnumerable<DriveGrantRequest> Drives { get; set; }

}