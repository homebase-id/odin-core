using System.Collections.Generic;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;

namespace Odin.Services.Membership.Circles;

/// <summary>
/// A circle an app creates for itself.  No id, owning app or enrolment tier: the server picks the id, the caller
/// is the owner, and the circle is only ever granted explicitly.
/// </summary>
public class CreateAppCircleRequest
{
    public string Name { get; set; }

    public string Description { get; set; }

    public string Emoji { get; set; }

    /// <summary>Only drives the calling app owns.</summary>
    public IEnumerable<DriveGrantRequest> DriveGrants { get; set; }

    /// <summary>Only permission keys the calling app holds.</summary>
    public PermissionSet Permissions { get; set; }
}
