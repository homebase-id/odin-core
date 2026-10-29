using System.Collections.Generic;
using Odin.Core;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;

namespace Odin.Services.Membership.Circles;

/// <summary>
/// A circle an app creates for itself.  No id, owning app or enrolment tier: the server picks the id, the caller
/// is the owner, and the circle is only ever granted explicitly.
/// </summary>
/// <remarks>
/// Also the shape of a circle an app declares in its install request (<c>AppRegistrationRequest.Circles</c>,
/// #1939), where <see cref="Slug"/> is required and the grants are bounded by the registration rather than the
/// caller.  Declaring one does not make it reach every device: only the first install shows the install screen,
/// so an app that needs its circle everywhere still calls <c>circles/create</c> with the same slug.
/// </remarks>
public class CreateAppCircleRequest
{
    public string Name { get; set; }

    /// <summary>
    /// The circle's address within the calling app (#1939).  When the app already holds a circle with this slug,
    /// the create returns that circle's id and changes nothing -- so setup on a second device, with no local
    /// state, finds the circle the first one made.  Optional: without it every call makes a new circle, with a
    /// slug made from <see cref="Name"/>.
    /// </summary>
    public OdinSlug Slug { get; set; }

    public string Description { get; set; }

    public string Emoji { get; set; }

    /// <summary>
    /// Only access the calling app holds itself, and on a drive it does not own, only Read with the storage key.
    /// </summary>
    public IEnumerable<DriveGrantRequest> DriveGrants { get; set; }

    /// <summary>Only permission keys the calling app holds.</summary>
    public PermissionSet Permissions { get; set; }
}
