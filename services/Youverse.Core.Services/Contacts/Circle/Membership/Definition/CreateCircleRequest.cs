using System;
using System.Collections.Generic;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;

namespace Youverse.Core.Services.Contacts.Circle.Membership.Definition;

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