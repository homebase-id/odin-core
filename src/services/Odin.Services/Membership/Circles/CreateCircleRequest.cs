using System;
using System.Collections.Generic;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;

namespace Odin.Services.Membership.Circles;

public class CreateCircleRequest
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    
    public string Description { get; set; }
    
    /// <summary>
    /// The drives granted to members of this Circle
    /// </summary>
    public IEnumerable<DriveGrantRequest> DriveGrants { get; set; }

    /// <summary>
    /// The permissions to be granted to members of this Circle
    /// </summary>
    public PermissionSet Permissions { get; set; }
}